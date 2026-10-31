using System.Collections.Concurrent;
using System.Security.Cryptography;
using BdoComboSetup.Core.Assets.Pack;

namespace BdoComboSetup.Tests;

/// <summary>The reader of a complete pack: the fixture (written by Python), the key vector, the refusals and the cache.</summary>
public sealed class AssetPackTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    [Fact]
    public void Default_key_is_the_one_the_python_builder_derives()
    {
        // Known answer from src/bdo_combo/assetpack/format.py (default_key_material). A change here breaks parity.
        Assert.Equal(
            "6ff99b0db3ab3957e8a559bb0b06b3358512da7b5831f9507cd0f3cee713d20e",
            Convert.ToHexString(AssetPackFormat.DefaultKeys.EncryptionKey).ToLowerInvariant());
        Assert.Equal(
            "1ce7645aa583e8cfb651b24f4ad68988954bfdbc6202733a0b973cee334aa53b",
            Convert.ToHexString(AssetPackFormat.DefaultKeys.NonceKey).ToLowerInvariant());
    }

    [Fact]
    public void Every_fixture_entry_reads_back_with_its_expected_length_and_hash()
    {
        using var pack = AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger);
        using var expected = AssetPackFixtures.ExpectedValues();
        var entries = expected.RootElement.GetProperty("entries").EnumerateArray().ToList();

        Assert.Null(pack.Part);
        Assert.Equal(entries.Select(entry => entry.GetProperty("key").GetString()!), pack.Keys);
        foreach (var entry in entries)
        {
            var key = entry.GetProperty("key").GetString()!;
            var bytes = Get(pack, key);

            Assert.Equal(entry.GetProperty("originalLength").GetInt32(), bytes.Length);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    [Fact]
    public void Verify_passes_on_the_fixture_and_counts_every_entry()
    {
        using var pack = AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger);

        var verification = pack.Verify();

        Assert.True(verification.IsValid);
        Assert.Equal(9, verification.Entries);
    }

    [Fact]
    public void An_unknown_key_is_false_and_not_an_error()
    {
        using var pack = AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger);

        Assert.False(pack.Contains("skill-404.png"));
        Assert.False(pack.TryGet("skill-404.png", out _));
    }

    [Fact]
    public void A_flipped_byte_in_an_entry_is_refused_and_reported_by_verify()
    {
        var bytes = File.ReadAllBytes(AssetPackFixtures.Synthetic);
        bytes[AssetPackFormat.HeaderSize + 20] ^= 0x01; // inside the first blob, which belongs to the first entry in key order
        using var pack = AssetPack.Open(new MemoryStream(bytes), _log.Logger);

        var verification = pack.Verify();

        var failure = Assert.Single(verification.Failures);
        Assert.Equal(AssetPackErrorCodes.EntryAuthentication, failure.Code);
        Assert.False(pack.TryGet(failure.Key, out _));
        Assert.Equal(8, pack.Keys.Count(key => key != failure.Key && pack.TryGet(key, out _)));
    }

    [Fact]
    public void A_flipped_byte_in_the_index_is_refused_on_open()
    {
        var bytes = File.ReadAllBytes(AssetPackFixtures.Synthetic);
        bytes[^5] ^= 0x80;

        var error = Assert.Throws<AssetPackException>(() => AssetPack.Open(new MemoryStream(bytes), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.IndexAuthentication, error.Code);
    }

    [Fact]
    public void A_wrong_key_is_refused_on_open_before_any_entry_is_read()
    {
        var wrong = AssetPackFormat.DeriveKeys(new byte[32]);

        var error = Assert.Throws<AssetPackException>(() =>
            AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger, new AssetPackOptions(Keys: wrong)));

        Assert.Equal(AssetPackErrorCodes.IndexAuthentication, error.Code);
    }

    [Fact]
    public void A_truncated_or_extended_file_is_refused()
    {
        var bytes = File.ReadAllBytes(AssetPackFixtures.Synthetic);

        Assert.Equal(AssetPackErrorCodes.Truncated, Assert.Throws<AssetPackException>(() => AssetPack.Open(new MemoryStream(bytes[..^1]), _log.Logger)).Code);
        Assert.Equal(AssetPackErrorCodes.Truncated, Assert.Throws<AssetPackException>(() => AssetPack.Open(new MemoryStream(bytes.Concat(new byte[] { 0 }).ToArray()), _log.Logger)).Code);
    }

    [Fact]
    public void A_file_that_is_not_a_pack_is_refused()
    {
        var bytes = File.ReadAllBytes(AssetPackFixtures.Synthetic);
        bytes[0] = (byte)'X';

        var error = Assert.Throws<AssetPackException>(() => AssetPack.Open(new MemoryStream(bytes), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.BadMagic, error.Code);
    }

    [Fact]
    public void A_part_is_refused_by_the_runtime_reader_and_opened_only_when_allowed()
    {
        var part = Path.Combine(AssetPackFixtures.Parts, "assets.part02.bdoa");

        var refused = Assert.Throws<AssetPackException>(() => AssetPack.Open(File.OpenRead(part), _log.Logger));
        Assert.Equal(AssetPackErrorCodes.IsPart, refused.Code);

        using var allowed = AssetPack.Open(File.OpenRead(part), _log.Logger, new AssetPackOptions(AllowParts: true));
        Assert.Equal(new PartInfo(2, 4, AssetPackFormat.DefaultSetId), allowed.Part);
    }

    [Fact]
    public void A_non_seekable_stream_is_read_in_full()
    {
        var bytes = File.ReadAllBytes(AssetPackFixtures.Synthetic);

        using var pack = AssetPack.Open(new ForwardOnlyStream(bytes), _log.Logger);

        Assert.Equal(9, pack.Keys.Count);
        Assert.True(pack.Verify().IsValid);
    }

    [Fact]
    public void Concurrent_reads_return_the_same_bytes()
    {
        // A tiny cache forces most reads to decode again, so the decoding path runs in parallel.
        using var pack = AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger, new AssetPackOptions(CacheBudgetBytes: 64));
        var keys = pack.Keys.ToList();
        var reference = keys.ToDictionary(key => key, key => Get(pack, key), StringComparer.Ordinal);
        var mismatches = new ConcurrentBag<string>();

        Parallel.For(0, 200, index =>
        {
            var key = keys[index % keys.Count];
            if (!pack.TryGet(key, out var bytes) || !bytes.AsSpan().SequenceEqual(reference[key]))
            {
                mismatches.Add(key);
            }
        });

        Assert.Empty(mismatches);
    }

    [Fact]
    public void A_disposed_pack_refuses_further_use()
    {
        var pack = AssetPack.Open(File.OpenRead(AssetPackFixtures.Synthetic), _log.Logger);
        pack.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = pack.Keys);
        Assert.Throws<ObjectDisposedException>(() => pack.TryGet("catalog.json", out _));
    }

    [Theory]
    [InlineData("skill-1.png", true)]
    [InlineData("guide-tamer.awakening.json", true)]
    [InlineData("", false)]
    [InlineData("Skill-1.png", false)]
    [InlineData("../skill.png", false)]
    [InlineData("-skill.png", false)]
    public void Keys_follow_the_format_rules(string key, bool valid)
    {
        Assert.Equal(valid, AssetPackFormat.IsValidKey(System.Text.Encoding.ASCII.GetBytes(key)));
    }

    private static byte[] Get(IAssetPack pack, string key)
    {
        Assert.True(pack.TryGet(key, out var bytes), key);
        return bytes!;
    }

    /// <summary>A stream that cannot seek, like a network stream: the pack must copy it first.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

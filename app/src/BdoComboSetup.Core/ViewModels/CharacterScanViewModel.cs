using System.Collections.ObjectModel;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One character of the scan review: the name and the class the OCR read, ready to be corrected before saving. The pcid
/// comes from the position of the row in the game's <c>CharacterOrderList</c>, never from the OCR.
/// </summary>
public sealed class CharacterScanRow : ObservableObject
{
    private readonly ILocalizer _localizer;
    private string _name = string.Empty;
    private ClassChoice _class;

    public CharacterScanRow(
        int order,
        CharacterEntry entry,
        CharacterRowCandidate reading,
        int? detectedClass,
        IReadOnlyList<ClassChoice> classes,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(localizer);

        _localizer = localizer;
        Order = order;
        Pcid = entry.Pcid;
        Level = reading.Level;
        LevelText = reading.Level is int level ? localizer.Format("Characters.ScanLevel", level) : string.Empty;
        // The memo comes from the game's own file, which has it complete: the OCR only reads the name and the level.
        Memo = string.IsNullOrWhiteSpace(entry.Label) ? null : entry.Label.Trim();
        PreviousName = entry.Name;
        Name = string.IsNullOrWhiteSpace(reading.Name) ? entry.Name : reading.Name.Trim();

        Classes = classes;
        var classType = detectedClass ?? entry.ClassType;
        // The icon first; when it was not recognized, the class the game files already give the character (or "Sin clase").
        // Either way the row stays automatic, so a later, clearer reading can still replace it.
        _class = classes.FirstOrDefault(choice => choice.ClassType == classType) ?? classes[0];
        _autoClass = _class;
    }

    /// <summary>The class the row started with (what the scan or the roster knew); never null.</summary>
    private readonly ClassChoice _autoClass;

    /// <summary>True while the class is still the automatic one (nothing picked by hand).</summary>
    public bool IsAutomaticClass => ReferenceEquals(_class, _autoClass);

    public string ClassName => _class.Name;

    /// <summary>The class icon of the row, used when the scan reads it again.</summary>
    public Func<Stream?> OpenClassIcon => _class.OpenIcon;

    /// <summary>One reading of the row when the list scrolls: the level always; the class only while it is still the
    /// automatic one and the new reading is not "Sin clase", so a later, clearer pass fills a gap without overriding a
    /// choice made by hand.</summary>
    public void ApplyReading(CharacterRowCandidate reading, int? detectedClass)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Level = reading.Level;
        LevelText = reading.Level is int level ? _localizer.Format("Characters.ScanLevel", level) : string.Empty;

        if (IsAutomaticClass && detectedClass is int classType)
        {
            var choice = Classes.FirstOrDefault(item => item.ClassType == classType);
            if (choice is not null)
            {
                _class = choice;
                OnPropertyChanged(nameof(Class));
                OnPropertyChanged(nameof(ClassName));
                OnPropertyChanged(nameof(OpenClassIcon));
                OnPropertyChanged(nameof(IsAutomaticClass));
            }
        }
    }

    /// <summary>The position in the list, counted from 1 (the order the game shows).</summary>
    public int Order { get; }

    /// <summary>The character this row belongs to, taken from the order of the game's character list.</summary>
    public string Pcid { get; }

    public int? Level { get; private set; }

    /// <summary>"Nv. 61", or empty when the level was not read.</summary>
    public string LevelText { get; private set; }

    /// <summary>The player's memo the game shows above the level ("FARM", "KUTUM"...), read from the game's file; null when the character has none.</summary>
    public string? Memo { get; }

    public string MemoText => Memo ?? string.Empty;

    public bool HasMemo => Memo is not null;

    /// <summary>The name the roster showed before the scan, for comparison.</summary>
    public string PreviousName { get; }

    /// <summary>"Ahora: ..." with the previous name, shown under the name box.</summary>
    public string PreviousText => _localizer.Format("Characters.ScanPrevious", PreviousName);

    public bool HasPreviousName => !string.Equals(Name, PreviousName, StringComparison.Ordinal);

    public IReadOnlyList<ClassChoice> Classes { get; }

    /// <summary>The name to save. Empty means "no name": the roster falls back to the game label.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(HasPreviousName));
            }
        }
    }

    /// <summary>The class to save. The "no class" choice keeps the automatic inference.</summary>
    public ClassChoice Class
    {
        get => _class;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _class, value);
            }
        }
    }
}

/// <summary>
/// The OCR scan of the character selection screen: it prepares the roster (the pcid order), takes the pages that the
/// capture reads while the player scrolls, and turns them into a review table. Saving writes only the app's own
/// %LOCALAPPDATA%\BDOTempo\characters.json through <see cref="ICharacterSource"/>; the game files are never touched.
/// <para>
/// If the number of rows read does not equal the number of characters, no table is built: the page says so instead of
/// pairing the names with the wrong pcids.
/// </para>
/// </summary>
public sealed partial class CharacterScanViewModel : ObservableObject
{
    /// <summary>The class id of the "no class" choice. It must not be a real class: the Warrior is class 0.</summary>
    public const int NoClassType = -1;

    private readonly ICharacterSource _characters;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly CharacterRowAccumulator _accumulator = new();
    private IReadOnlyList<CharacterEntry> _entries = [];
    private string? _account;

    public CharacterScanViewModel(
        ICharacterSource characters,
        IReadOnlyList<ClassChoice> classes,
        ILocalizer localizer,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _characters = characters;
        _localizer = localizer;
        _logger = logger;

        var noClass = new ClassChoice(
            NoClassType,
            localizer.Get("Characters.ScanNoClass"),
            static () => null,
            BilingualText.Same(localizer.Get("Characters.ScanNoClass")));
        Classes = [noClass, .. classes];
    }

    /// <summary>The class choices of the review, "no class" first.</summary>
    public IReadOnlyList<ClassChoice> Classes { get; }

    /// <summary>The review rows, in the game's character order.</summary>
    public ObservableCollection<CharacterScanRow> Rows { get; } = [];

    /// <summary>True while the capture runs and rows are being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanFinish), nameof(CanCancel))]
    private bool _isScanning;

    /// <summary>"12 de 35": the rows read and how many characters the account has.</summary>
    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>A line under the count when a page could not be placed, or when every character was read.</summary>
    [ObservableProperty]
    private string _pageHint = string.Empty;

    public bool HasPageHint => PageHint.Length > 0;

    partial void OnPageHintChanged(string value) => OnPropertyChanged(nameof(HasPageHint));

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private StatusSeverity _severity = StatusSeverity.Information;

    [ObservableProperty]
    private bool _isMessageOpen;

    /// <summary>True when the review table is shown (the scan ended with the right number of rows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanCancel))]
    private bool _hasReview;

    [ObservableProperty]
    private bool _canSave;

    /// <summary>A scan can start when nothing is running and no review is open.</summary>
    public bool CanStart => !IsScanning && !HasReview;

    /// <summary>The scan can be finished while it is running.</summary>
    public bool CanFinish => IsScanning;

    /// <summary>A running scan or an open review can be discarded.</summary>
    public bool CanCancel => IsScanning || HasReview;

    /// <summary>
    /// Loads the roster and the account, and clears the previous scan. False (with the reason on the page) when there is no
    /// account or no character.
    /// </summary>
    public bool Prepare()
    {
        _account = _characters.ActiveAccount();
        if (_account is null)
        {
            ShowMessage(StatusSeverity.Error, _localizer.Get("Characters.ScanNoAccount"));
            return false;
        }

        try
        {
            _entries = _characters.Load(_account).OrderBy(entry => entry.Order).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("character_scan.roster_failed", "The characters could not be read for the scan", ex);
            ShowMessage(StatusSeverity.Error, _localizer.Get("Characters.ScanLoadFailed"));
            return false;
        }

        if (_entries.Count == 0)
        {
            ShowMessage(StatusSeverity.Warning, _localizer.Get("Characters.ScanNoCharacters"));
            return false;
        }

        _accumulator.Reset();
        Rows.Clear();
        HasReview = false;
        CanSave = false;
        IsMessageOpen = false;
        PageHint = string.Empty;
        UpdateCount();

        _logger.Info("character_scan.prepared", "Character scan prepared", new Dictionary<string, object?>
        {
            ["characters"] = _entries.Count,
        });
        return true;
    }

    /// <summary>Starts a new scan: the rows of the previous one are dropped.</summary>
    public void BeginScan()
    {
        _accumulator.Reset();
        Rows.Clear();
        HasReview = false;
        CanSave = false;
        IsMessageOpen = false;
        PageHint = string.Empty;
        IsScanning = true;
        UpdateCount();
        _logger.Info("character_scan.started", "Character scan started");
    }

    /// <summary>
    /// Adds one page of visible rows (what the OCR just read). The page must overlap the rows already known; a page that
    /// cannot be placed adds nothing, so a repeated or misread page never invents characters.
    /// </summary>
    public void AddPage(IReadOnlyList<CharacterRowReading> page, double? scrollFraction = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!IsScanning)
        {
            return;
        }

        var result = _accumulator.AddPage(page, _entries.Count);
        PageHint = result.Outcome switch
        {
            RowMergeOutcome.Unplaced => _localizer.Get("Characters.ScanPageUnplaced"),
            RowMergeOutcome.Full => _localizer.Format("Characters.ScanPageFull", _entries.Count),
            _ => string.Empty,
        };

        _logger.Debug("character_scan.page", "Rows merged by the scan", new Dictionary<string, object?>
        {
            ["outcome"] = result.Outcome.ToString(),
            ["visible"] = page.Count,
            ["added"] = result.Added,
            ["total"] = result.Total,
            ["characters"] = _entries.Count,
            ["scroll"] = scrollFraction is { } fraction ? Math.Round(fraction, 3) : null,
            ["top"] = page.FirstOrDefault()?.Row.Name,
            ["bottom"] = page.LastOrDefault()?.Row.Name,
        });

        UpdateCount();
    }

    /// <summary>
    /// Ends the capture and builds the review. False (with the reason) when the account file and the captured rows do not
    /// have the same number of characters: pairing them anyway would save the names on the wrong pcids.
    /// </summary>
    public bool Finish()
    {
        IsScanning = false;
        var read = _accumulator.Rows;
        if (read.Count != _entries.Count)
        {
            Rows.Clear();
            HasReview = false;
            CanSave = false;
            ShowMessage(
                StatusSeverity.Warning,
                _localizer.Format("Characters.ScanCountMismatch", _entries.Count - read.Count, _entries.Count));
            _logger.Warn("character_scan.count_mismatch", "The scan did not read the same number of characters as the account file", new Dictionary<string, object?>
            {
                ["read"] = read.Count,
                ["characters"] = _entries.Count,
            });
            return false;
        }

        // The rows are paired with the pcids by position. The memos are the only thing the game's file and the screen both
        // show, so when most of the file's memos are not on their rows the pairing is wrong: stop instead of saving names on
        // the wrong characters. Characters without a memo, or with the same memo, are simply not counted: position decides.
        var labelled = 0;
        var agreed = 0;
        for (var index = 0; index < read.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(_entries[index].Label))
            {
                continue;
            }

            labelled++;
            if (CharacterRowAccumulator.SimilarText(_entries[index].Label, read[index].Row.Memo))
            {
                agreed++;
            }
        }

        if (labelled >= 2 && agreed * 2 < labelled)
        {
            Rows.Clear();
            HasReview = false;
            CanSave = false;
            ShowMessage(StatusSeverity.Warning, _localizer.Format("Characters.ScanMemoMismatch", agreed, labelled));
            _logger.Warn("character_scan.memo_mismatch", "The memos on screen do not match the game file by position", new Dictionary<string, object?>
            {
                ["agreed"] = agreed,
                ["labelled"] = labelled,
            });
            return false;
        }

        Rows.Clear();
        for (var index = 0; index < read.Count; index++)
        {
            Rows.Add(new CharacterScanRow(index + 1, _entries[index], read[index].Row, read[index].ClassType, Classes, _localizer));
        }

        HasReview = Rows.Count > 0;
        CanSave = HasReview;
        ShowMessage(StatusSeverity.Success, _localizer.Format("Characters.ScanReady", Rows.Count));
        _logger.Info("character_scan.finished", "Character scan finished", new Dictionary<string, object?>
        {
            ["rows"] = Rows.Count,
            ["with_level"] = Rows.Count(row => row.Level is not null),
            ["memos_in_file"] = labelled,
            ["memos_agreeing"] = agreed,
        });
        return true;
    }

    /// <summary>Stops the scan without a review (the capture ended, or the user cancelled).</summary>
    public void Stop()
    {
        if (IsScanning)
        {
            _logger.Info("character_scan.stopped", "Character scan stopped");
        }

        IsScanning = false;
    }

    /// <summary>How many characters have a reading so far.</summary>
    public int RowsRead => _accumulator.Count;

    /// <summary>
    /// Shows why the scan cannot read anything. The count is dropped when nothing was read: "0 de 3" says nothing, the reason does.
    /// </summary>
    public void ReportFailure(string messageKey)
    {
        IsScanning = false;
        if (_accumulator.Count == 0)
        {
            CountText = string.Empty;
        }

        ShowMessage(StatusSeverity.Error, _localizer.Get(messageKey));
    }

    /// <summary>Drops everything read: the user cancelled the scan.</summary>
    public void Discard()
    {
        _accumulator.Reset();
        Rows.Clear();
        HasReview = false;
        CanSave = false;
        IsMessageOpen = false;
        PageHint = string.Empty;
        IsScanning = false;
    }

    /// <summary>
    /// Saves the reviewed names and classes into the app's character file. Returns how many characters were saved. The
    /// game files are not touched.
    /// </summary>
    public int Save()
    {
        if (!HasReview || _account is null)
        {
            return 0;
        }

        var saved = 0;
        foreach (var row in Rows)
        {
            var name = CharacterOverrideStore.NormalizeName(row.Name);
            var classType = row.Class.ClassType != NoClassType ? row.Class.ClassType : (int?)null;
            if (name is null && classType is null)
            {
                continue;
            }

            _characters.SaveOverride(_account, row.Pcid, name, classType);
            saved++;
        }

        HasReview = false;
        CanSave = false;
        ShowMessage(StatusSeverity.Success, _localizer.Format("Characters.ScanSaved", saved));
        _logger.Info("character_scan.saved", "Names read by the scan saved", new Dictionary<string, object?>
        {
            ["characters"] = saved,
        });
        return saved;
    }

    /// <summary>
    /// Gets a character. The class picked by hand is kept; a row whose class is still the automatic one takes the new
    /// reading when there is one.
    /// </summary>
    public void RefreshRow(CharacterScanRow row, int? detectedClass)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (detectedClass is null || !IsScanning)
        {
            return;
        }

        row.ApplyReading(new CharacterRowCandidate(row.Name, row.Level, 0, 0, 0, 0, 0, row.Memo), detectedClass);
    }

    private void UpdateCount() =>
        CountText = _entries.Count > 0
            ? _localizer.Format("Characters.ScanCount", Math.Min(_accumulator.Count, _entries.Count), _entries.Count)
            : _localizer.Format("Characters.ScanCountOnly", _accumulator.Count);

    private void ShowMessage(StatusSeverity severity, string text)
    {
        Message = text;
        Severity = severity;
        IsMessageOpen = true;
    }
}

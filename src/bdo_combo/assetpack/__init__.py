"""The ``.bdoa`` asset pack: one proprietary, encrypted archive of the app's icons, slot art and JSON bundle.

Format: ``app/docs/asset-pack-format.md``. The C# reader is ``BdoComboSetup.Core/Assets/Pack``.
Command line: ``python -m bdo_combo.assetpack pack|verify`` (see ``mise run pack-assets``).
"""

from .format import AssetPackError, KeyMaterial, PartInfo, default_key_material, derive_key_material
from .reader import AssetPackReader
from .parts import assemble_parts, build_parts, verify_parts, write_parts
from .writer import PackInput, PackResult, build_pack, write_pack

__all__ = [
    "AssetPackError",
    "AssetPackReader",
    "KeyMaterial",
    "PartInfo",
    "PackInput",
    "PackResult",
    "assemble_parts",
    "build_parts",
    "verify_parts",
    "write_parts",
    "build_pack",
    "default_key_material",
    "derive_key_material",
    "write_pack",
]

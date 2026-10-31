namespace BdoComboSetup.Tests;

/// <summary>
/// Rows copied from UserCache\330\gamevariable.xml of the 3440x1440 sample (only UI geometry; no account data).
/// The chat rows of index 32 are shortened to their start tag. The block lines keep the file's tab indentation.
/// </summary>
internal static class UiLayoutFixtures
{
    public const int GameWidth = 3440;
    public const int GameHeight = 1440;

    public const string Sample = """
        <CheckQuestList Version="1"/>
        <UIData Version="2">
        	<UIData Index="32" IsShow="true" WindowIndex="0" IsUsing="true" IsCombinedToMainPanel="true" PendingType="LeftBottom" PosX="19" PosY="708" RelativePosX="0.09912790358" RelativePosY="0.7487855554" SizeX="644" SizeY="694"/>
        	<UIData Index="32" IsShow="false" WindowIndex="1" IsUsing="true" IsCombinedToMainPanel="true" PendingType="LeftBottom" PosX="151" PosY="292" RelativePosX="0.1049418598" RelativePosY="0.8736988306" SizeX="420" SizeY="222"/>
        	<UIData Index="159" IsShow="true" PendingType="LeftBottom" PosX="684" PosY="331" RelativePosX="0.2497093081" RelativePosY="0.8736988306"/>
        	<UIData Index="55" IsShow="false" PendingType="LeftTop" PosX="225" PosY="35" RelativePosX="0.07412790507" RelativePosY="0.04510756582"/>
        	<UIData Index="139" IsShow="true" PendingType="LeftTop" PosX="1" PosY="88" RelativePosX="0.007703488227" RelativePosY="0.0787647441"/>
        	<UIData Index="86" IsShow="false"/>
        	<UIData Index="2" IsShow="true"/>
        	<UIData Index="13" IsShow="true" PendingType="LeftBottom" PosX="1524" PosY="176" RelativePosX="0.4989825487" RelativePosY="0.891047895"/>
        	<UIData Index="9" IsShow="true" PendingType="RightTop" PosX="433" PosY="20" RelativePosX="0" RelativePosY="0" SizeX="426" SizeY="462" Alpha="0" Zoom="0"/>
        	<UIData Index="85" IsShow="true" PendingType="RightBottom" PosX="1487" PosY="168" RelativePosX="0.5745639801" RelativePosY="0.8990284801"/>
        	<UIData Index="100" IsShow="true" PendingType="LeftBottom" PosX="1449" PosY="80" RelativePosX="0.4284883738" RelativePosY="0.9663428068" IsPinHold="false"/>
        	<UIData Index="91" IsShow="true" PendingType="LeftBottom" PosX="1522" PosY="78" RelativePosX="0.4497092962" RelativePosY="0.9677307606" IsPinHold="false"/>
        	<UIData Index="92" IsShow="true" PendingType="LeftBottom" PosX="1591" PosY="79" RelativePosX="0.4697674513" RelativePosY="0.9670367837" IsPinHold="false"/>
        	<UIData Index="93" IsShow="true" PendingType="LeftBottom" PosX="1664" PosY="79" RelativePosX="0.4909883738" RelativePosY="0.9670367837" IsPinHold="false"/>
        	<UIData Index="94" IsShow="true" PendingType="RightBottom" PosX="1706" PosY="79" RelativePosX="0.5113372207" RelativePosY="0.9670367837" IsPinHold="false"/>
        	<UIData Index="95" IsShow="true" PendingType="RightBottom" PosX="1631" PosY="78" RelativePosX="0.5331395268" RelativePosY="0.9677307606" IsPinHold="false"/>
        	<UIData Index="96" IsShow="true" PendingType="RightBottom" PosX="1559" PosY="78" RelativePosX="0.5540697575" RelativePosY="0.9677307606" IsPinHold="false"/>
        	<UIData Index="97" IsShow="true" PendingType="RightBottom" PosX="1488" PosY="78" RelativePosX="0.5747092962" RelativePosY="0.9677307606" IsPinHold="false"/>
        	<UIData Index="21" IsShow="false" PendingType="RightTop" PosX="364" PosY="621" RelativePosX="0.9385174513" RelativePosY="0.6894517541" SizeX="305" SizeY="745"/>
        	<UIData Index="113" IsShow="true" PendingType="RightTop" PosX="367" PosY="510" RelativePosX="0.9376453757" RelativePosY="0.3813323975" SizeX="305" SizeY="79"/>
        	<UIData Index="141" IsShow="true" PendingType="RightTop" PosX="933" PosY="0" RelativePosX="0" RelativePosY="0"/>
        	<UIData Index="99999999999" IsShow="true" PendingType="LeftTop" PosX="1" PosY="1"/>
        	<UIData Index="51" IsShow="false">
        		<ChatType key="Notice" value="false" colorIndexValue="-1"/>
        	</UIData>
        </UIData>
        <UISettingPreset Version="1">
        	<UISettingPreset0/>
        	<UISettingPreset0 Index="9" IsShow="true" PendingType="LeftTop" PosX="3007" PosY="20" RelativePosX="0.936047" RelativePosY="0.174185" SizeX="-1" SizeY="-1" Alpha="0" Zoom="0"/>
        	<UISettingPreset0 Index="21" IsShow="false" PendingType="LeftTop" PosX="3076" PosY="621" RelativePosX="0.938517" RelativePosY="0.689452" SizeX="-1" SizeY="-1"/>
        	<UISettingPreset0 Index="13" IsShow="true" PendingType="LeftTop" PosX="1524" PosY="1264" RelativePosX="0.498983" RelativePosY="0.891048"/>
        	<UISettingPreset0 Index="85" IsShow="true" PendingType="LeftTop" PosX="1953" PosY="1272" RelativePosX="0.574564" RelativePosY="0.899028"/>
        	<UISettingPreset0 Index="100" IsShow="true" PendingType="LeftTop" PosX="1449" PosY="1360" RelativePosX="0.428488" RelativePosY="0.966343" IsPinHold="false"/>
        	<UISettingPreset0 Index="91" IsShow="true" PendingType="LeftTop" PosX="1522" PosY="1362" RelativePosX="0.449709" RelativePosY="0.967731" IsPinHold="false"/>
        	<UISettingPreset0 Index="94" IsShow="true" PendingType="LeftTop" PosX="1734" PosY="1361" RelativePosX="0.511337" RelativePosY="0.967037" IsPinHold="false"/>
        	<UISettingPreset0 Index="113" IsShow="true" PendingType="LeftTop" PosX="3073" PosY="510" RelativePosX="0.937645" RelativePosY="0.381332" SizeX="-1" SizeY="-1"/>
        	<UISettingPreset0 Index="139" IsShow="true" PendingType="LeftTop" PosX="1" PosY="88" RelativePosX="0.00770349" RelativePosY="0.0787647"/>
        	<UISettingPreset0 Index="141" IsShow="true" PendingType="LeftTop" PosX="2507" PosY="0" RelativePosX="0.801453" RelativePosY="0.0208189"/>
        	<UISettingPreset1/>
        	<UISettingPreset1 Index="13" IsShow="false" PendingType="LeftTop" PosX="1531" PosY="1271" RelativePosX="0.501017" RelativePosY="0.895906"/>
        	<UISettingPreset1 Index="91" IsShow="true" PendingType="LeftTop" PosX="1522" PosY="1362" RelativePosX="0.449709" RelativePosY="0.967731" IsPinHold="false"/>
        </UISettingPreset>
        <UISettingRevert Index="2" IsShow="true"/>
        """;

    /// <summary>Only the preset block, for tests that need a file without a General block.</summary>
    public const string PresetOnly = """
        <UISettingPreset Version="1">
        	<UISettingPreset2 Index="13" IsShow="true" PendingType="LeftTop" PosX="10" PosY="20"/>
        </UISettingPreset>
        """;
}

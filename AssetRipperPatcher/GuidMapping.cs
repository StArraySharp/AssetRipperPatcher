using System.Diagnostics.CodeAnalysis;
using AssetRipper.Primitives;

namespace AssetRipperPatcher;

public static class GuidMapping
{
    private static readonly Dictionary<long, UnityGuid> TMPFileIDAndGuid = new Dictionary<long, UnityGuid>
    {
        // TMPro.TMP_Dropdown
        { 1148083418L, UnityGuid.Parse("7b743370ac3e4ec2a1668f5455a8ef8a") },
        // TMPro.TMP_InputField
        { -1620774994L, UnityGuid.Parse("2da0c512f12947e489f739169773d7ca") },
        // TMPro.TMP_Text
        { -1385168320L, UnityGuid.Parse("5143f58107604835ab1a5efa2d8818fd") },
        // TMPro.TextMeshPro
        { -806885394L, UnityGuid.Parse("9541d86e2fd84c1d9990edf0852d74ab") },
        // TMPro.TextMeshProUGUI
        { 1453722849L, UnityGuid.Parse("f4688fdb7df04437aeb418b961361dc5") },
        // TMPro.TMP_FontAsset
        { -667331979L, UnityGuid.Parse("71c1514a6bd24e1e882cebbe1904ce04") },
        // TMPro.TMP_StyleSheet
        { -1936749209L, UnityGuid.Parse("ab2114bdc8544297b417dfefe9f1e410") },
        // TMPro.TMP_SpriteAsset
        { 2019389346L, UnityGuid.Parse("84a92b25f83d49b9bc132d206b370281") },
        // TMPro.TMP_Settings
        { -395462249L, UnityGuid.Parse("2705215ac5b84b70bacc50632be6e391") },
        // TMPro.TMP_ColorGradient
        { 2108210716L, UnityGuid.Parse("54d21f6ece3b46479f0c328f8c6007e0") },
        // TMPro.TMP_SubMesh
        { 1330537494L, UnityGuid.Parse("07994bfe8b0e4adb97d706de5dea48d5") },
        // TMPro.TMP_SubMesh (nested)
        { 1908110080L, UnityGuid.Parse("07994bfe8b0e4adb97d706de5dea48d5") },
        // TMPro.TMP_SpriteAnimator
        { -907550668L, UnityGuid.Parse("6366ee97f6b541449155028b9487355a") }
    };
    private static readonly Dictionary<long, UnityGuid> UGUIFileIDAndGuid = new Dictionary<long, UnityGuid>
    {
        // UnityEngine.EventSystems.EventSystem
        { -619905303L, UnityGuid.Parse("76c392e42b5098c458856cdf6ecaaaa1") },
        // UnityEngine.EventSystems.EventTrigger
        { -1862395651L, UnityGuid.Parse("d0b148fe25e99eb48b9724523833bab1") },
        // UnityEngine.EventSystems.TriggerEvent
        { 362180543L, UnityGuid.Parse("d0b148fe25e99eb48b9724523833bab1") },
        // UnityEngine.EventSystems.Entry
        { -283727016L, UnityGuid.Parse("d0b148fe25e99eb48b9724523833bab1") },
        // UnityEngine.EventSystems.UIBehaviour
        { 479328781L, UnityGuid.Parse("0aaa057ce5566e940b18a0ccd0344693") },
        // UnityEngine.EventSystems.BaseInput
        { 1327945023L, UnityGuid.Parse("1511ccae7919cfc46b603b9b337fdc94") },
        // UnityEngine.EventSystems.BaseInputModule
        { -1837839079L, UnityGuid.Parse("1ea10891dd782154ca0fb67bce9e6f72") },
        // UnityEngine.EventSystems.PointerInputModule
        { -1235632416L, UnityGuid.Parse("cbab8835092323a4389f2dc7d8f6c781") },
        // UnityEngine.EventSystems.StandaloneInputModule
        { 1077351063L, UnityGuid.Parse("4f231c4fb786f3946a6b90b886c48677") },
        // UnityEngine.EventSystems.TouchInputModule
        { 1997211142L, UnityGuid.Parse("2d49b7c1bcd2e07499844da127be038d") },
        // UnityEngine.EventSystems.BaseRaycaster
        { -1394661563L, UnityGuid.Parse("426106349a0ff964fa4e7178c1d3a4f5") },
        // UnityEngine.EventSystems.Physics2DRaycaster
        { -1690312454L, UnityGuid.Parse("56666c5a40171f54783dd416a44f42bf") },
        // UnityEngine.EventSystems.PhysicsRaycaster
        { -768656878L, UnityGuid.Parse("c49b4cc203aa6414fae5c798d1d0e7d6") },
        // UnityEngine.UIElements.PanelEventHandler
        { -1610670888L, UnityGuid.Parse("8a410b213209442fb15a69799f0240a5") },
        // UnityEngine.UIElements.PanelRaycaster
        { -19100109L, UnityGuid.Parse("7a79c4e23d4d4b64bfa5fa9e6671a460") },
        // UnityEngine.UIElements.WorldDocumentRaycaster
        { -1534336401L, UnityGuid.Parse("2c727e4c005e5114fb3348a38c4cebbf") },
        // UnityEngine.UI.AnimationTriggers
        { -1029687192L, UnityGuid.Parse("c67ac6e40bbb6fe47a095b949b609ce0") },
        // UnityEngine.UI.Button
        { 1392445389L, UnityGuid.Parse("4e29b1a8efbd4b44bb3f3716e73f07ff") },
        // UnityEngine.UI.ButtonClickedEvent
        { -2053097296L, UnityGuid.Parse("4e29b1a8efbd4b44bb3f3716e73f07ff") },
        // UnityEngine.UI.Dropdown
        { 853051423L, UnityGuid.Parse("0d0b652f32a2cc243917e4028fa0f046") },
        // UnityEngine.UI.DropdownItem
        { -132589691L, UnityGuid.Parse("0d0b652f32a2cc243917e4028fa0f046") },
        // UnityEngine.UI.OptionData
        { -629596264L, UnityGuid.Parse("0d0b652f32a2cc243917e4028fa0f046") },
        // UnityEngine.UI.OptionDataList
        { -361509218L, UnityGuid.Parse("0d0b652f32a2cc243917e4028fa0f046") },
        // UnityEngine.UI.DropdownEvent
        { -950048149L, UnityGuid.Parse("0d0b652f32a2cc243917e4028fa0f046") },
        // UnityEngine.UI.FontData
        { -1611522143L, UnityGuid.Parse("13cd966480ec3354bb318ee1aeccff6f") },
        // UnityEngine.UI.Graphic
        { 383007879L, UnityGuid.Parse("a6238e9452bfc704f82ff36791fe1a45") },
        // UnityEngine.UI.GraphicRaycaster
        { 1301386320L, UnityGuid.Parse("dc42784cf147c0c48a680349fa168899") },
        // UnityEngine.UI.Image
        { -765806418L, UnityGuid.Parse("fe87c0e1cc204ed48ad3b37840f39efc") },
        // UnityEngine.UI.InputField
        { 575553740L, UnityGuid.Parse("d199490a83bb2b844b9695cbf13b01ef") },
        // UnityEngine.UI.SubmitEvent
        { 681916893L, UnityGuid.Parse("d199490a83bb2b844b9695cbf13b01ef") },
        // UnityEngine.UI.EndEditEvent
        { 617338464L, UnityGuid.Parse("d199490a83bb2b844b9695cbf13b01ef") },
        // UnityEngine.UI.OnChangeEvent
        { -111384078L, UnityGuid.Parse("d199490a83bb2b844b9695cbf13b01ef") },
        // UnityEngine.UI.Mask
        { -1200242548L, UnityGuid.Parse("31a19414c41e5ae4aae2af33fee712f6") },
        // UnityEngine.UI.MaskableGraphic
        { -1493381411L, UnityGuid.Parse("976acc75bfafe594cb01142ba21947be") },
        // UnityEngine.UI.CullStateChangedEvent
        { -456143525L, UnityGuid.Parse("976acc75bfafe594cb01142ba21947be") },
        // UnityEngine.UI.RawImage
        { -98529514L, UnityGuid.Parse("1344c3c82d62a2a41a3576d8abb8e3ea") },
        // UnityEngine.UI.RectMask2D
        { -146154839L, UnityGuid.Parse("3312d7739989d2b4e91e6319e9a96d76") },
        // UnityEngine.UI.Scrollbar
        { -2061169968L, UnityGuid.Parse("2a4db7a114972834c8e4117be1d82ba3") },
        // UnityEngine.UI.ScrollEvent
        { 1830187589L, UnityGuid.Parse("2a4db7a114972834c8e4117be1d82ba3") },
        // UnityEngine.UI.ScrollRect
        { 1367256648L, UnityGuid.Parse("1aa08ab6e0800fa44ae55d278d1423e3") },
        // UnityEngine.UI.ScrollRectEvent
        { 1007853787L, UnityGuid.Parse("1aa08ab6e0800fa44ae55d278d1423e3") },
        // UnityEngine.UI.Selectable
        { -234403039L, UnityGuid.Parse("7a98125502f715b4b83cfb77b434e436") },
        // UnityEngine.UI.Slider
        { -113659843L, UnityGuid.Parse("67db9e8f0e2ae9c40bc1e2b64352a6b4") },
        // UnityEngine.UI.SliderEvent
        { -743917596L, UnityGuid.Parse("67db9e8f0e2ae9c40bc1e2b64352a6b4") },
        // UnityEngine.UI.Text
        { 708705254L, UnityGuid.Parse("5f7201a12d95ffc409449d95f23cf332") },
        // UnityEngine.UI.Toggle
        { 2109663825L, UnityGuid.Parse("9085046f02f69544eb97fd06b6048fe2") },
        // UnityEngine.UI.ToggleEvent
        { 131858844L, UnityGuid.Parse("9085046f02f69544eb97fd06b6048fe2") },
        // UnityEngine.UI.ToggleGroup
        { -1184210157L, UnityGuid.Parse("2fafe2cfe61f6974895a912c3755e8f1") },
        // UnityEngine.UI.AspectRatioFitter
        { -1254083943L, UnityGuid.Parse("86710e43de46f6f4bac7c8e50813a599") },
        // UnityEngine.UI.CanvasScaler
        { 1980459831L, UnityGuid.Parse("0cd44c1031e13a943bb63640046fad76") },
        // UnityEngine.UI.ContentSizeFitter
        { 1741964061L, UnityGuid.Parse("3245ec927659c4140ac4f8d17403cc18") },
        // UnityEngine.UI.GridLayoutGroup
        { -2095666955L, UnityGuid.Parse("8a8695521f0d02e499659fee002a26c2") },
        // UnityEngine.UI.HorizontalLayoutGroup
        { -405508275L, UnityGuid.Parse("30649d3a9faa99c48a7b1166b86bf2a0") },
        // UnityEngine.UI.HorizontalOrVerticalLayoutGroup
        { 812294440L, UnityGuid.Parse("a5c214e5846a99242b348c37e49b2f59") },
        // UnityEngine.UI.LayoutElement
        { 1679637790L, UnityGuid.Parse("306cc8c2b49d7114eaa3623786fc2126") },
        // UnityEngine.UI.LayoutGroup
        { -555945567L, UnityGuid.Parse("cb7c939a806f03341b682c180dc13f08") },
        // UnityEngine.UI.VerticalLayoutGroup
        { 1297475563L, UnityGuid.Parse("59f8146938fff824cb5fd77236b75775") },
        // UnityEngine.UI.PositionAsUV1
        { 1849938685L, UnityGuid.Parse("718966f960cb50643986195e5fe953ca") },
        // UnityEngine.UI.BaseMeshEffect
        { 1649306735L, UnityGuid.Parse("718966f960cb50643986195e5fe953ca") },
        // UnityEngine.UI.Outline
        { -900027084L, UnityGuid.Parse("e19747de3f5aca642ab2be37e372fb86") },
        // UnityEngine.UI.Shadow
        { 1573420865L, UnityGuid.Parse("cfabb0440166ab443bba8876756fdfa9") }
    };

    public const string TMPVersion = "3.0.9";
    public const string UGUIVersion = "2.0.0";
    
    public static bool TryGet(long fileID, out UnityGuid guid)
    {
        guid = UnityGuid.Zero;
        if (TMPFileIDAndGuid.TryGetValue(fileID, out var newGuid))
        {
            guid = newGuid;
        }
        else if (Program.IsUnity6 && UGUIFileIDAndGuid.TryGetValue(fileID, out var newGuid2))
        {
            guid = newGuid2;
        }
        return guid != UnityGuid.Zero;
    }
}
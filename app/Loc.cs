using System.Globalization;
using System.Windows;

namespace ClaudeSessionOverlay;

/// <summary>화면 언어. 상단바 오른쪽 클릭 메뉴에서 고르고 overlay-state.json에 기억한다.</summary>
public enum UiLanguage { System, Korean, English }

/// <summary>
/// 한국어/영어 문구. 코드는 Loc.T("한국어", "English")로, XAML은 {DynamicResource L_키}로 쓴다
/// (바꾸면 XAML 문구는 바로, 목록 줄은 다음 새로고침에 다시 그려진다).
/// </summary>
internal static class Loc
{
    public static bool IsEnglish { get; private set; }

    /// <summary>언어가 바뀌었을 때(음성 바꾸기 등)</summary>
    public static event Action? Changed;

    public static string T(string korean, string english) => IsEnglish ? english : korean;

    // XAML에서 쓰는 문구
    private static readonly Dictionary<string, (string Ko, string En)> Xaml = new()
    {
        ["L_AppTitle"] = ("Claude 세션", "Claude Sessions"),
        ["L_WindowTitle"] = ("Claude 세션 오버레이", "Claude Session Overlay"),
        ["L_Open"] = ("열기", "Open"),
        ["L_MarkSeen"] = ("확인 처리 (목록에서 숨기기)", "Mark as seen (hide from list)"),
        ["L_ToggleFavorite"] = ("즐겨찾기 추가/해제", "Add/remove favorite"),
        ["L_OpenFolder"] = ("폴더 열기", "Open folder"),
        ["L_Theme"] = ("테마", "Theme"),
        ["L_Dark"] = ("다크", "Dark"),
        ["L_Light"] = ("라이트", "Light"),
        ["L_FollowWindows"] = ("Windows 설정 따라가기", "Follow Windows"),
        ["L_Language"] = ("언어 (Language)", "Language (언어)"),
        ["L_ShowSeen"] = ("확인한 세션 다시 보기", "Show seen sessions again"),
        ["L_HideToTray"] = ("트레이로 숨기기", "Hide to tray"),
        ["L_Weekly"] = ("주간", "Week"),
        ["L_Minimize"] = ("최소화 (트레이 아이콘으로 다시 열기)", "Minimize (reopen from the tray icon)"),
        ["L_Close"] = ("종료", "Quit"),
        ["L_FavoriteTip"] = ("즐겨찾기: 확인해도 항상 표시", "Favorite: always shown, even after you've seen it"),
        ["L_Empty"] = ("확인할 세션이 없어요", "Nothing to check"),
        ["L_SessionLimit"] = ("세션 한도", "Session"),
        ["L_SessionLimitTip"] = ("5시간 세션 한도", "5-hour session limit"),
    };

    public static void Apply(UiLanguage language)
    {
        IsEnglish = language switch
        {
            UiLanguage.English => true,
            UiLanguage.Korean => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ko",
        };
        var resources = Application.Current.Resources;
        foreach (var (key, text) in Xaml) resources[key] = IsEnglish ? text.En : text.Ko;
        Changed?.Invoke();
    }
}

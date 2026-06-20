#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 팀 테스트용 원클릭 빌드. 반드시 '개발 빌드(Development Build)'로 만들어
/// QA 점프 오버레이(백틱 ` 키)와 콘솔이 빌드에 포함되게 한다.
/// (정식 릴리스 빌드는 #if UNITY_EDITOR || DEVELOPMENT_BUILD 가드 때문에 QA 점프가 빠진다)
///
/// 사용: 상단 메뉴 [빌드] ▸ [팀 테스트 빌드 (개발빌드·QA점프 포함)] 클릭.
/// 출력: Build/Day1to14/PassportPlease.exe
/// </summary>
public static class DevBuildScript
{
    private const string OutDir = "Build/Day1to14";
    private const string ExeName = "PassportPlease.exe";

    [MenuItem("빌드/팀 테스트 빌드 (개발빌드·QA점프 포함)")]
    public static void BuildDevWin64()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[DevBuild] Build Settings 에 활성 씬이 없습니다. 씬을 추가하세요.");
            return;
        }

        Directory.CreateDirectory(OutDir);
        string outPath = Path.Combine(OutDir, ExeName);

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development, // 개발빌드 → DEVELOPMENT_BUILD 정의 → QA 점프(`) 포함
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"[DevBuild] 성공 → {outPath}  ({s.totalSize / (1024 * 1024)} MB)  씬 {scenes.Length}개");
        else
            Debug.LogError($"[DevBuild] 실패: {s.result} (오류 {s.totalErrors}개)");
    }
}
#endif

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 14일 씬 분리 자동화(Step 2~4).
/// 1) 현재 ImmigrationScene 의 루트 CoreRig(카메라/캔버스/이벤트시스템/매니저 포함)를
///    Assets/Prefabs/CoreRig.prefab 로 추출(SaveAsPrefabAssetAndConnect → 내부 참조 보존).
/// 2) Day1Scene ~ Day14Scene 을 생성하고 각 씬에 CoreRig 프리팹 인스턴스 1개만 배치.
/// 3) 빌드 세팅 갱신: TitleScene, MainMenuScene, BriefingScene, ResultScene, Day1~14 만 enabled.
///    ImmigrationScene/SampleScene 은 빌드에서 제외(파일 보존).
///
/// 멱등 실행: 센티넬 파일(Assets/Editor/.daysplit_run)이 있으면 도메인 리로드 시 1회 실행 후 센티넬 삭제.
/// 메뉴(Tools/Inspection/Split Day Scenes)로 수동 실행도 가능.
/// </summary>
public static class DaySceneSplitter
{
    private const string PrefabPath = "Assets/Prefabs/CoreRig.prefab";
    private const string CoreRigName = "CoreRig";
    private const string SourceScenePath = "Assets/Scenes/ImmigrationScene.unity";
    private const int DayCount = 14;
    private const string SentinelPath = "Assets/Editor/.daysplit_run";

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        if (!File.Exists(SentinelPath)) return;
        Debug.Log($"[DaySceneSplitter] Hook: sentinel 발견 — 첫 update 틱에 Run() 예약. cwd={Directory.GetCurrentDirectory()}");
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        EditorApplication.update -= RunOnce;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.update += RunOnce; // 임포트/컴파일 중이면 다음 틱으로
            return;
        }
        try { Run(); }
        finally
        {
            if (File.Exists(SentinelPath)) File.Delete(SentinelPath);
            AssetDatabase.Refresh();
        }
    }

    [MenuItem("Tools/Inspection/Split Day Scenes")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DaySceneSplitter] Play 모드에서는 실행 불가. 중단.");
            return;
        }

        // ── 1) ImmigrationScene 로드 + CoreRig 프리팹 추출 ──
        Scene src = EditorSceneManager.GetActiveScene();
        if (src.path != SourceScenePath)
        {
            src = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        }

        GameObject coreRig = FindRoot(src, CoreRigName);
        if (coreRig == null)
        {
            Debug.LogError($"[DaySceneSplitter] '{CoreRigName}' 루트를 ImmigrationScene 에서 찾지 못했습니다. 중단.");
            return;
        }

        // 추출 전 ImmigrationManager 참조 스냅샷(검증 로그)
        LogManagerRefs("추출 전(씬 인스턴스)", coreRig);

        Directory.CreateDirectory("Assets/Prefabs");
        // 이미 프리팹 인스턴스면 다시 만들지 않는다(멱등).
        GameObject prefabAsset;
        if (PrefabUtility.IsAnyPrefabInstanceRoot(coreRig)
            && PrefabUtility.GetPrefabAssetType(coreRig) != PrefabAssetType.NotAPrefab)
        {
            prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("[DaySceneSplitter] CoreRig 가 이미 프리팹 인스턴스 — 추출 생략.");
        }
        else
        {
            prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(coreRig, PrefabPath, InteractionMode.AutomatedAction);
            if (prefabAsset == null)
            {
                Debug.LogError("[DaySceneSplitter] CoreRig 프리팹 추출 실패. 중단.");
                return;
            }
            EditorSceneManager.MarkSceneDirty(src);
            EditorSceneManager.SaveScene(src);
            Debug.Log($"[DaySceneSplitter] CoreRig.prefab 추출 + ImmigrationScene 저장 완료.");
        }

        // 추출 후 프리팹 에셋의 ImmigrationManager 참조 검증
        VerifyPrefabRefs();

        // ── 2) Day1Scene ~ Day14Scene 생성(각 씬 = CoreRig 인스턴스 1개) ──
        List<string> created = new List<string>();
        for (int day = 1; day <= DayCount; day++)
        {
            string scenePath = $"Assets/Scenes/Day{day}Scene.unity";
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, s);
            inst.name = CoreRigName;
            EditorSceneManager.MoveGameObjectToScene(inst, s);
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s, scenePath);
            created.Add(scenePath);
        }
        Debug.Log($"[DaySceneSplitter] Day1~Day{DayCount} 씬 {created.Count}개 생성 완료.");

        // ── 3) 빌드 세팅 갱신 ──
        UpdateBuildSettings();

        // 마무리: 첫 Day 씬 로드 상태로 둔다(Play 금지 — 그냥 열어둠).
        EditorSceneManager.OpenScene("Assets/Scenes/Day1Scene.unity", OpenSceneMode.Single);
        Debug.Log("[DaySceneSplitter] 완료. Play 금지 상태로 Day1Scene 열림.");
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go;
        }
        return null;
    }

    private static void LogManagerRefs(string tag, GameObject root)
    {
        ImmigrationManager mgr = root.GetComponentInChildren<ImmigrationManager>(true);
        if (mgr == null) { Debug.LogWarning($"[DaySceneSplitter] {tag}: ImmigrationManager 없음"); return; }
        SerializedObject so = new SerializedObject(mgr);
        string[] fields = { "fadePanel", "inspectionController", "newsPopup", "rulebookPopup", "dialogueLogPopup", "endingPanel" };
        int ok = 0;
        foreach (string f in fields)
        {
            SerializedProperty p = so.FindProperty(f);
            bool set = p != null && p.objectReferenceValue != null;
            if (set) ok++;
            else Debug.LogWarning($"[DaySceneSplitter] {tag}: ImmigrationManager.{f} = NULL");
        }
        Debug.Log($"[DaySceneSplitter] {tag}: ImmigrationManager 참조 {ok}/6 연결됨.");
    }

    private static void VerifyPrefabRefs()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogError("[DaySceneSplitter] 검증: 프리팹 로드 실패"); return; }
        LogManagerRefs("추출 후(프리팹 에셋)", prefab);
    }

    private static void UpdateBuildSettings()
    {
        // 원하는 enabled 목록(순서): Title, MainMenu, Briefing, Result, Day1~Day14
        List<string> wanted = new List<string>
        {
            "Assets/Scenes/TitleScene.unity",
            "Assets/Scenes/MainMenuScene.unity",
            "Assets/Scenes/BriefingScene.unity",
            "Assets/Scenes/ResultScene.unity",
        };
        for (int day = 1; day <= DayCount; day++)
            wanted.Add($"Assets/Scenes/Day{day}Scene.unity");

        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>();
        foreach (string path in wanted)
        {
            bool exists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
            if (!exists)
            {
                Debug.LogWarning($"[DaySceneSplitter] 빌드 세팅: '{path}' 에셋 없음 — 건너뜀.");
                continue;
            }
            list.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = list.ToArray();

        Debug.Log("[DaySceneSplitter] 빌드 세팅 갱신 완료(enabled 순서):");
        for (int i = 0; i < list.Count; i++)
            Debug.Log($"  [{i}] {list[i].path} (enabled={list[i].enabled})");
    }
}
#endif

using UnityEditor;
using UnityEngine;

/// <summary>
/// 게임 UI/서류/캐릭터 리소스 PNG들을 Sprite(2D and UI, Single) 로 자동 임포트한다.
/// - Assets/Resources/Characters/ : 캐릭터 초상(photo_ref) — Resources.Load&lt;Sprite&gt;("Characters/...")
/// - Assets/Resources/UI/         : 배경 등 화면 UI 스프라이트 — Resources.Load&lt;Sprite&gt;("UI/...")
/// - Assets/Resources/Documents/  : 여권 펼침 템플릿 등 서류 배경 — Resources.Load&lt;Sprite&gt;("Documents/...")
/// 모두 SpriteImportMode.Single 로 강제해 단일 Sprite 로 로드되게 한다(외부 Multiple 설정 교정).
/// 큰 원본은 메모리 과다를 막기 위해 maxTextureSize 를 제한한다(배경은 큰 화면이므로 2048).
/// </summary>
public sealed class CharacterSpritePostprocessor : AssetPostprocessor
{
    private const string CharactersFolder = "Assets/Resources/Characters/";
    private const string UiFolder = "Assets/Resources/UI/";
    private const string DocumentsFolder = "Assets/Resources/Documents/";

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');

        bool isCharacter = path.StartsWith(CharactersFolder);
        bool isUi = path.StartsWith(UiFolder);
        bool isDocument = path.StartsWith(DocumentsFolder);
        if (!isCharacter && !isUi && !isDocument)
        {
            return;
        }

        TextureImporter importer = assetImporter as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        // 배경(UI)은 전체 화면을 채우므로 해상도를 더 허용. 캐릭터/서류는 1024 로 제한.
        importer.maxTextureSize = isUi ? 2048 : 1024;
    }
}

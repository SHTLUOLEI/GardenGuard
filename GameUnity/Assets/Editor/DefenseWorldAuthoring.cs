using System;
using System.IO;
using GameLogic.Defense90;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class DefenseWorldAuthoring
{
    public const string Folder = "Assets/BundleAssets/Defense90/World";
    public const string WorldPath = "Assets/BundleAssets/Prefabs/Defense/DefenseBattlefield.prefab";
    public const string UiPath = "Assets/BundleAssets/UI/Defense/DefenseWindow.prefab";

    [CliCommand("defense_author_world", "Create Defense scene sprites, water material and world prefab; migrate only the battle UI. Saves a backup before replacing the UI prefab.")]
    public static object Build([CliArg("dry_run", "Preview target paths without writing")] bool dryRun = true, [CliArg("confirm", "Apply the migration")] bool confirm = false)
    {
        if (dryRun) return new { status = "dry_run", create = Folder, migrate = UiPath,
            remove = new[] { "m_rimgArena" }, cameraLayer = "Water (4)", ruleChanges = 0 };
        if (!confirm || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Confirm in stopped Editor required");
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == UiPath && stage.scene.isDirty)
            throw new InvalidOperationException("Save the current battle Prefab Stage before migration");
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(WorldPath));
        AssetDatabase.Refresh();
        string initialPath = Folder + "/DefenseBattlefield.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(initialPath) != null && AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath) == null)
        {
            string error = AssetDatabase.MoveAsset(initialPath, WorldPath);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }
        string backup = Path.GetFullPath("../GameRelease/Defense90/WorldTA/Before/DefenseWindow.authoring.prefab");
        if (!File.Exists(backup)) File.Copy(UiPath, backup);
        var timeline = AssetDatabase.LoadAssetAtPath<DefenseSkillTimeline>(Folder + "/DefenseWaterTimeline.asset");
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<DefenseSkillTimeline>();
            AssetDatabase.CreateAsset(timeline, Folder + "/DefenseWaterTimeline.asset");
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/DefenseWaterMaterial.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Defense90/WorldWater"));
            AssetDatabase.CreateAsset(material, Folder + "/DefenseWaterMaterial.mat");
        }
        var effect = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/DefenseEffectMaterial.mat");
        if (effect == null)
        {
            effect = new Material(Shader.Find("Defense90/EffectSprite"));
            AssetDatabase.CreateAsset(effect, Folder + "/DefenseEffectMaterial.mat");
        }
        string whitePath = Folder + "/WaterQuad.png";
        if (!File.Exists(whitePath))
        {
            var white = new Texture2D(2, 2); white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); white.Apply();
            File.WriteAllBytes(whitePath, white.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(white);
            AssetDatabase.ImportAsset(whitePath);
        }
        // YooAsset 按无扩展名文件名生成地址，Sprite 与源纹理必须使用不同名称。
        Sprite quad = SpriteAsset("WaterQuadSprite", whitePath, new Rect(0, 0, 1, 1));
        string atlasPath = "Assets/BundleAssets/UIRaw/Atlas/Defense/DefenseUnits.png";
        var root = new GameObject("DefenseBattlefield") { layer = 4 };
        try
        {
            var view = root.AddComponent<DefenseBattlefield>();
            view.UnitAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            view.TurretSprite = SpriteAsset("Turret", atlasPath, new Rect(0, .5f, .5f, .5f));
            view.RunnerSprite = SpriteAsset("Rabbit", atlasPath, new Rect(.5f, .5f, .5f, .5f));
            view.TankSprite = SpriteAsset("Turtle", atlasPath, new Rect(0, 0, .5f, .5f));
            view.StarSprite = SpriteAsset("Star", "Assets/BundleAssets/UIRaw/Atlas/Defense/UI/IconStar.png", new Rect(0, 0, 1, 1));
            view.WhiteSprite = quad; view.Timeline = timeline; view.WaterMaterial = material; view.EffectMaterial = effect;
            view.Turret = Renderer(root.transform, "Turret", view.TurretSprite, null, 50);
            view.Turret.transform.localPosition = DefenseBattlefield.ToWorld(DefenseRound.BASE_POSITION);
            view.Turret.transform.localScale = Vector3.one * 1.48f;
            var camera = new GameObject("BattleCamera", typeof(Camera)); camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(3.2f, 5.4f, -10);
            view.WorldCamera = camera.GetComponent<Camera>();
            view.WorldCamera.orthographic = true; view.WorldCamera.orthographicSize = 6.4f;
            view.WorldCamera.cullingMask = 1 << 4; view.WorldCamera.depth = 0;
            view.WorldCamera.clearFlags = CameraClearFlags.SolidColor; view.WorldCamera.backgroundColor = new Color(.2f, .38f, .25f);
            view.Background = Renderer(root.transform, "Garden", SpriteAsset("Garden", "Assets/BundleAssets/UIRaw/Raw/Defense/DefenseArenaV3.png", new Rect(0, 0, 1, 1)), null, -1000);
            view.Background.transform.localPosition = new Vector3(3.2f, 5.4f, 2);
            view.Background.transform.localScale = Vector3.one * 7.2f;
            var skill = new GameObject("WaterSkill") { layer = 4 }; skill.transform.SetParent(root.transform, false);
            view.Skill = skill.AddComponent<DefenseSkillPresentation>(); view.Skill.Timeline = timeline;
            view.Skill.Area = Renderer(skill.transform, "Range", quad, material, 10);
            view.Skill.Column = Renderer(skill.transform, "Column", quad, material, 700);
            view.Skill.Splash = Renderer(skill.transform, "Impact", quad, material, 710);
            view.Skill.Sample(Vector3.zero, 1, -1, 1, 1, 1, true);
            PrefabUtility.SaveAsPrefabAsset(root, WorldPath, out bool saved);
            if (!saved) throw new InvalidOperationException("World prefab save failed");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }

        var ui = PrefabUtility.LoadPrefabContents(UiPath);
        try
        {
            // 这些节点属于本次迁移目标；其余 HUD 引用和布局继续保留。
            var background = ui.transform.Find("m_rimgArena");
            if (background != null) UnityEngine.Object.DestroyImmediate(background.gameObject);
            foreach (Graphic graphic in ui.GetComponentsInChildren<Graphic>(true))
                if (graphic.GetComponentInParent<Selectable>() == null) graphic.raycastTarget = false;
            string iconPath = Folder + "/SkillWaterApikl.png";
            if (File.Exists(iconPath))
            {
                AssetDatabase.ImportAsset(iconPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                importer.textureType = TextureImporterType.Sprite; importer.alphaIsTransparency = true; importer.SaveAndReimport();
                foreach (Image icon in ui.GetComponentsInChildren<Image>(true))
                    if (icon.sprite != null && icon.sprite.name == "IconSkill") icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            }
            PrefabUtility.SaveAsPrefabAsset(ui, UiPath, out bool saved);
            if (!saved) throw new InvalidOperationException("UI prefab save failed");
        }
        finally { PrefabUtility.UnloadPrefabContents(ui); }
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(WorldPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(UiPath, ImportAssetOptions.ForceUpdate);
        var result = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath).GetComponent<DefenseBattlefield>();
        return new { status = "created", world = WorldPath, camera = result.WorldCamera.orthographic, backup };
    }

    [CliCommand("defense_author_scenes", "Place the world prefab in Defense90 and Defense90Stress, replace their unbound legacy DefenseGame, and save with backups. Does not change startup settings.")]
    public static object Scenes([CliArg("dry_run", "Preview scene migration")] bool dryRun = true, [CliArg("confirm", "Apply scene migration")] bool confirm = false)
    {
        string[] names = { "Defense90", "Defense90Stress" };
        if (dryRun) return new { status = "dry_run", scenes = names, prefab = WorldPath, removeComponent = "unbound DefenseGame", disable = "legacy Main Camera renderer" };
        if (!confirm || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Confirm in stopped Editor required");
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (string name in names)
        {
            string path = "Assets/BundleAssets/Scenes/Defense90/" + name + ".unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Save pending scene edits first: " + path);
            string backup = Path.GetFullPath("../GameRelease/Defense90/WorldTA/Before/" + name + ".unity");
            if (!File.Exists(backup)) File.Copy(path, backup);
            bool hasWorld = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<DefenseBattlefield>() != null) hasWorld = true;
                var legacy = root.GetComponent<DefenseGame>();
                if (legacy != null)
                {
                    var serialized = new SerializedObject(legacy);
                    if (serialized.FindProperty("m_root").objectReferenceValue != null)
                        throw new InvalidOperationException("Unexpected bound scene controller: " + root.name);
                    UnityEngine.Object.DestroyImmediate(legacy);
                }
                var camera = root.GetComponent<Camera>();
                if (camera != null && root.name == "Main Camera") camera.enabled = false;
            }
            if (!hasWorld) PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath), scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed: " + path);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
        return new { status = "saved", scenes = names, prefab = WorldPath };
    }

    private static Sprite SpriteAsset(string name, string texturePath, Rect normalized)
    {
        string path = Folder + "/" + name + ".asset";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        var rect = new Rect(normalized.x * texture.width, normalized.y * texture.height, normalized.width * texture.width, normalized.height * texture.height);
        sprite = Sprite.Create(texture, rect, Vector2.one * .5f, rect.width, 0, SpriteMeshType.FullRect);
        sprite.name = name; AssetDatabase.CreateAsset(sprite, path);
        return sprite;
    }

    private static SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Material material, int order)
    {
        var go = new GameObject(name) { layer = 4 }; go.transform.SetParent(parent, false);
        var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
        if (material != null) renderer.sharedMaterial = material;
        return renderer;
    }
}

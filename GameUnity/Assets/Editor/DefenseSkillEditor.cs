using System.Collections.Generic;
using System.IO;
using GameLogic;
using GameLogic.Defense90;
using GameProto;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

// 预览相机和对象只存在 PreviewRenderUtility 的隔离场景，不污染当前关卡。
// 三个页签：时间轴（技能分阶段并排对比两档）、特效库（全部战斗特效两档缩略图）、材质与校验。
// 所有预览共用运行时 Shader、时间轴资源和档位表，所见即运行时表现。
public sealed class DefenseSkillEditor : EditorWindow
{
    private enum Tab { Timeline, Library, Materials }
    private enum View { Compare, Full, Simple }
    private enum Drops { None, Skill, Hit, Kill }

    // 特效库条目：Mode 为 WorldWater 形态或 EffectSprite 形态；Mode -1 表示按档位表 Glow 选择彗星或水滴。
    private sealed class Entry
    {
        public string Name;
        public bool Effect, FullOnly;
        public int Mode;
        public Vector2 Scale = Vector2.one;
        public Drops Drops;
        public float Jump = -1;
    }

    private const string ASSETS_PATH = "Assets/BundleAssets/Defense90/DefenseAssets.asset";
    private const int THUMB = 96;
    private static readonly string[] s_tabNames = { "时间轴", "特效库", "材质与校验" };
    private static readonly string[] s_viewNames = { "并排对比", "仅完整", "仅简化" };
    private static readonly Color s_sand = new Color(.91f, .77f, .47f), s_dark = new Color(.08f, .13f, .17f);
    private static readonly Vector3 s_libraryPoint = new Vector3(0, 60, 0);
    private static readonly int ModeId = Shader.PropertyToID("_Mode"), ProgressId = Shader.PropertyToID("_Progress"),
        OpacityId = Shader.PropertyToID("_Opacity"), DetailId = Shader.PropertyToID("_Detail"),
        WaterId = Shader.PropertyToID("_Water"), FoamId = Shader.PropertyToID("_Foam"), DeepId = Shader.PropertyToID("_Deep"),
        DropletsId = Shader.PropertyToID("_Droplets"), ShapeId = Shader.PropertyToID("_Shape");

    private PreviewRenderUtility m_preview;
    private DefenseBattlefield m_view;
    private DefenseRules m_rules;
    private DefenseVfxTierConfig m_full, m_simple;
    private Editor m_timelineEditor;
    private readonly List<Editor> m_materialEditors = new List<Editor>();
    private readonly List<string> m_issues = new List<string>();
    private readonly Dictionary<int, RenderTexture> m_thumbs = new Dictionary<int, RenderTexture>();
    private Entry[] m_entries;
    private SpriteRenderer m_hit, m_kill, m_shot, m_libraryWater, m_libraryEffect;
    private RenderTexture m_leftTexture, m_rightTexture;
    private Tab m_tab;
    private View m_mode;
    private bool m_sandGround = true, m_playing;
    private float m_age;
    private double m_lastTime, m_lastRepaint;
    private Vector2 m_scroll;
    private string m_error;
    private float Duration => m_rules.TelegraphDuration + m_rules.ReleaseDuration + m_rules.HitDuration;

    [MenuItem("DGame/Defense/水系技能时间轴")]
    public static void Open() => GetWindow<DefenseSkillEditor>("水系特效 · 完整/简化");

    [CliCommand("defense_skill_editor", "Open the shared runtime water-skill timeline preview")]
    public static object OpenCommand() { Open(); return new { status = "opened" }; }

    private void OnEnable()
    {
        string folder = Path.Combine(Application.dataPath, "BundleAssets/Configs/Binary");
        if (!Directory.Exists(folder)) { m_error = "缺少配置目录：" + folder; return; }
        var tables = new Tables(file => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(folder, file + ".bytes"))));
        m_rules = DefenseConfigMgr.CreateRules(tables, DefenseConfigMgr.MODE_NORMAL);
        m_full = tables.TbDefenseVfxTierConfig.GetOrDefaultNoStatic(DefenseConfigMgr.TIER_FULL);
        m_simple = tables.TbDefenseVfxTierConfig.GetOrDefaultNoStatic(DefenseConfigMgr.TIER_SIMPLE);
        if (m_full == null || m_simple == null) { m_error = "守点特效档位表缺少完整或简化档。"; return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseWorldAuthoring.WorldPath);
        if (prefab == null) { m_error = "请先运行 defense_author_world 创建场景资源。"; return; }
        m_preview = new PreviewRenderUtility();
        var instance = Instantiate(prefab); instance.hideFlags = HideFlags.HideAndDontSave;
        m_preview.AddSingleGO(instance); m_view = instance.GetComponent<DefenseBattlefield>();
        m_view.WorldCamera.enabled = false; m_view.Background.enabled = false; m_view.Turret.enabled = false;
        // 普攻样本：普通命中、击杀命中、水弹，与技能同屏便于看清档位差异。
        m_hit = Sample(instance.transform, "PreviewHit", new Vector3(-3.4f, -1.6f, 0), 1.2f, m_view.WaterMaterial);
        m_kill = Sample(instance.transform, "PreviewKill", new Vector3(3.4f, -1.6f, 0), 1.6f, m_view.WaterMaterial);
        m_shot = Sample(instance.transform, "PreviewShot", new Vector3(3.4f, 2.6f, 0), .5f, m_view.WaterMaterial);
        m_shot.transform.localScale = new Vector3(.5f, .9f, 1) / m_view.WhiteSprite.bounds.size.x;
        m_libraryWater = Sample(instance.transform, "LibraryWater", s_libraryPoint, 1, m_view.WaterMaterial);
        if (m_view.EffectMaterial != null)
            m_libraryEffect = Sample(instance.transform, "LibraryEffect", s_libraryPoint, 1, m_view.EffectMaterial);
        m_preview.camera.orthographic = true;
        m_preview.camera.clearFlags = CameraClearFlags.SolidColor;
        m_timelineEditor = Editor.CreateEditor(m_view.Timeline);
        BuildEntries();
        Validate();
        m_lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    private void BuildEntries()
    {
        float telegraph = m_rules.TelegraphDuration, release = m_rules.ReleaseDuration;
        m_entries = new[]
        {
            new Entry { Name = "范围提示", Mode = 0, Jump = telegraph * .5f },
            new Entry { Name = "水柱", Mode = 1, Scale = new Vector2(.45f, 1), Jump = telegraph + release * .5f },
            new Entry { Name = "命中扩散", Mode = 2, Drops = Drops.Skill, Jump = telegraph + release + m_rules.HitDuration * .3f },
            new Entry { Name = "普攻水花", Mode = 2, Drops = Drops.Hit, Scale = Vector2.one * .7f },
            new Entry { Name = "击杀水花", Mode = 2, Drops = Drops.Kill, Scale = Vector2.one * .9f },
            new Entry { Name = "普攻水弹", Mode = -1, Scale = new Vector2(.35f, .7f) },
            new Entry { Name = "敌方水弹", Mode = 3, Scale = Vector2.one * .35f },
            new Entry { Name = "击杀水泡", Effect = true, Mode = 0, Scale = Vector2.one * .8f },
            new Entry { Name = "金币闪光", Effect = true, Mode = 1, Scale = Vector2.one * .6f, FullOnly = true },
        };
    }

    private SpriteRenderer Sample(Transform parent, string name, Vector3 position, float size, Material material)
    {
        var go = new GameObject(name) { layer = parent.gameObject.layer, hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * size / m_view.WhiteSprite.bounds.size.x;
        var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = m_view.WhiteSprite; renderer.sortingOrder = 1600;
        renderer.sharedMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
        return renderer;
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (m_playing && m_rules != null) { m_age = Mathf.Repeat(m_age + (float)(now - m_lastTime), Duration); Repaint(); }
        // 特效库缩略图持续循环播放，限制在约 30 帧避免占满编辑器。
        else if (m_tab == Tab.Library && now - m_lastRepaint > .033) { m_lastRepaint = now; Repaint(); }
        m_lastTime = now;
    }

    private void OnGUI()
    {
        if (m_view == null) { EditorGUILayout.HelpBox(m_error ?? "预览初始化失败。", MessageType.Info); return; }
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        m_tab = (Tab)GUILayout.Toolbar((int)m_tab, s_tabNames, EditorStyles.toolbarButton, GUILayout.Width(240));
        GUILayout.FlexibleSpace();
        m_sandGround = GUILayout.Toggle(m_sandGround, "沙地背景", EditorStyles.toolbarButton, GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();
        if (m_tab == Tab.Timeline) DrawTimelineTab();
        else if (m_tab == Tab.Library) DrawLibraryTab();
        else DrawMaterialsTab();
    }

    #region 时间轴

    private void DrawTimelineTab()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(m_playing ? "暂停" : "播放", GUILayout.Width(60))) m_playing = !m_playing;
        if (GUILayout.Button("归零", GUILayout.Width(60))) { m_age = 0; m_playing = false; }
        GUILayout.Space(8);
        m_mode = (View)GUILayout.Toolbar((int)m_mode, s_viewNames, GUILayout.Width(240));
        EditorGUILayout.EndHorizontal();
        m_age = EditorGUILayout.Slider("时间（秒）", m_age, 0, Duration);
        DrawTimelineBar();
        Rect preview = GUILayoutUtility.GetRect(100, Mathf.Max(220, position.height * .48f), GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) DrawPreviews(preview);

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        DrawDifferences();
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("技能参数（修改可撤销）", EditorStyles.boldLabel);
        m_timelineEditor.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
    }

    private void DrawTimelineBar()
    {
        var bar = GUILayoutUtility.GetRect(100, 22, GUILayout.ExpandWidth(true));
        float[] lengths = { m_rules.TelegraphDuration, m_rules.ReleaseDuration, m_rules.HitDuration };
        string[] names = { "范围提示", "水柱落下", "命中扩散" };
        Color[] colors = { new Color(.12f, .4f, .45f), new Color(.12f, .45f, .7f), new Color(.18f, .6f, .65f) };
        float offset = 0;
        for (int i = 0; i < 3; i++)
        {
            var section = new Rect(bar.x + offset / Duration * bar.width, bar.y, lengths[i] / Duration * bar.width, bar.height);
            EditorGUI.DrawRect(section, colors[i]); GUI.Label(section, names[i]); offset += lengths[i];
        }
        EditorGUI.DrawRect(new Rect(bar.x + m_age / Duration * bar.width, bar.y, 2, bar.height), Color.white);
        if (Event.current.type == EventType.MouseDown && bar.Contains(Event.current.mousePosition))
        { m_age = Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width) * Duration; m_playing = false; Repaint(); }
    }

    private void DrawPreviews(Rect area)
    {
        if (m_mode != View.Compare) { RenderTimeline(area, m_mode == View.Full ? m_full : m_simple, ref m_leftTexture); return; }
        float half = area.width * .5f;
        RenderTimeline(new Rect(area.x, area.y, half - 1, area.height), m_full, ref m_leftTexture);
        RenderTimeline(new Rect(area.x + half + 1, area.y, half - 1, area.height), m_simple, ref m_rightTexture);
        EditorGUI.DrawRect(new Rect(area.x + half - 1, area.y, 2, area.height), Color.black);
    }

    private void RenderTimeline(Rect rect, DefenseVfxTierConfig tier, ref RenderTexture texture)
    {
        bool detail = tier.ShaderDetail;
        float[] lengths = { m_rules.TelegraphDuration, m_rules.ReleaseDuration, m_rules.HitDuration };
        m_view.Skill.Sample(Vector3.zero, m_rules.SkillRadius * .01f, m_age, lengths[0], lengths[1], lengths[2], detail);
        float attack = Mathf.Repeat(m_age / DefenseRound.IMPACT_LIFE, 1);
        SetActive(true);
        Water(m_hit, 2, attack, detail, tier.Fragments);
        Water(m_kill, 2, attack, detail, tier.KillFragments);
        Water(m_shot, tier.Glow ? 4 : 3, 0, detail, 0);
        Draw(rect, new Vector3(0, 1, -10), 4.2f, ref texture);

        var label = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 13 };
        GUI.Label(new Rect(rect.x + 6, rect.y + 4, rect.width, 20), TierName(tier), label);
        Caption(rect, m_hit.transform.position, "普攻水花");
        Caption(rect, m_kill.transform.position, "击杀水花");
        Caption(rect, m_shot.transform.position, "普攻水弹");
    }

    private void Caption(Rect rect, Vector3 world, string text)
    {
        Vector3 viewport = m_preview.camera.WorldToViewportPoint(world + Vector3.down * .9f);
        var point = new Vector2(rect.x + viewport.x * rect.width, rect.y + (1 - viewport.y) * rect.height);
        GUI.Label(new Rect(point.x - 40, point.y, 80, 18), text, new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperCenter });
    }

    private void DrawDifferences()
    {
        EditorGUILayout.LabelField("完整 / 简化 差异", EditorStyles.boldLabel);
        var header = new GUIStyle(EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("特效", header, GUILayout.Width(90)); GUILayout.Label("完整", header); GUILayout.Label("简化", header);
        EditorGUILayout.EndHorizontal();
        foreach (var row in DefenseVfxTierInfo.Rows)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(row.Effect, GUILayout.Width(90));
            GUILayout.Label(row.Full, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(row.Simple, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox($"档位表（守点特效档位表）：Shader细节 {Yes(m_full.ShaderDetail)}/{Yes(m_simple.ShaderDetail)}，" +
            $"命中水滴 {m_full.Fragments}/{m_simple.Fragments}，击杀水滴 {m_full.KillFragments}/{m_simple.KillFragments}，" +
            $"拖尾 {Yes(m_full.Glow)}/{Yes(m_simple.Glow)}，震屏 {Yes(m_full.Shake)}/{Yes(m_simple.Shake)}，飘字 {Yes(m_full.DamageText)}/{Yes(m_simple.DamageText)}",
            MessageType.None);
    }

    #endregion

    #region 特效库

    private void DrawLibraryTab()
    {
        EditorGUILayout.HelpBox("每项左为完整、右为简化，循环播放。点击技能条目跳到时间轴对应阶段。", MessageType.None);
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        float time = (float)EditorApplication.timeSinceStartup;
        int columns = Mathf.Max(1, (int)((position.width - 20) / (THUMB * 2 + 16)));
        for (int i = 0; i < m_entries.Length; i += columns)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = i; j < Mathf.Min(i + columns, m_entries.Length); j++) DrawEntry(j, time);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawEntry(int index, float time)
    {
        Entry entry = m_entries[index];
        EditorGUILayout.BeginVertical(GUILayout.Width(THUMB * 2 + 8));
        GUILayout.Label(entry.Name, EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(THUMB * 2 + 4, THUMB);
        var full = new Rect(rect.x, rect.y, THUMB, THUMB);
        var simple = new Rect(rect.x + THUMB + 4, rect.y, THUMB, THUMB);
        if (Event.current.type == EventType.Repaint)
        {
            RenderEntry(full, entry, m_full, Mathf.Repeat(time * .8f, 1), index * 2);
            if (entry.FullOnly) EditorGUI.DrawRect(simple, m_sandGround ? s_sand : s_dark);
            else RenderEntry(simple, entry, m_simple, Mathf.Repeat(time * .8f, 1), index * 2 + 1);
            if (entry.FullOnly) GUI.Label(simple, "简化档关闭", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
        }
        if (entry.Jump >= 0 && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        { m_age = entry.Jump; m_playing = false; m_tab = Tab.Timeline; Event.current.Use(); }
        EditorGUILayout.EndVertical();
    }

    private void RenderEntry(Rect rect, Entry entry, DefenseVfxTierConfig tier, float progress, int key)
    {
        m_view.Skill.Sample(Vector3.zero, 1, -1, 1, 1, 1, false);
        SetActive(false);
        bool detail = tier.ShaderDetail;
        SpriteRenderer target = entry.Effect ? m_libraryEffect : m_libraryWater;
        if (target == null) return;
        target.enabled = true;
        target.transform.localScale = new Vector3(entry.Scale.x, entry.Scale.y, 1) / m_view.WhiteSprite.bounds.size.x;
        if (entry.Effect)
        {
            Material material = target.sharedMaterial;
            material.SetFloat(ShapeId, entry.Mode); material.SetFloat(DetailId, detail ? 1 : 0);
            SetColors(material);
            target.color = new Color(1, 1, 1, progress);
        }
        else
        {
            int droplets = entry.Drops == Drops.Skill ? (detail ? m_view.Timeline.SkillDroplets : 0)
                : entry.Drops == Drops.Hit ? tier.Fragments : entry.Drops == Drops.Kill ? tier.KillFragments : 0;
            Water(target, entry.Mode < 0 ? (tier.Glow ? 4 : 3) : entry.Mode, progress, detail, droplets);
        }
        if (!m_thumbs.TryGetValue(key, out RenderTexture texture)) texture = null;
        Draw(rect, s_libraryPoint + new Vector3(0, 0, -10), .55f, ref texture);
        m_thumbs[key] = texture;
        target.enabled = false;
    }

    #endregion

    #region 材质与校验

    private void DrawMaterialsTab()
    {
        if (m_materialEditors.Count == 0)
        {
            var assets = AssetDatabase.LoadAssetAtPath<DefenseAssets>(ASSETS_PATH);
            foreach (var material in new[] { m_view.WaterMaterial, m_view.EffectMaterial, assets != null ? assets.PulseMaterial : null })
                if (material != null) m_materialEditors.Add(Editor.CreateEditor(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(material))));
        }
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("资源校验", EditorStyles.boldLabel);
        if (GUILayout.Button("重新校验", GUILayout.Width(80))) Validate();
        EditorGUILayout.EndHorizontal();
        if (m_issues.Count == 0) EditorGUILayout.HelpBox("Shader、材质、预制体引用与档位表均正常。", MessageType.Info);
        foreach (string issue in m_issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
        EditorGUILayout.HelpBox("颜色由时间轴资源在运行时写入，材质里只调默认值与形态参数；_Mode/_Progress 等由运行时驱动。", MessageType.None);
        foreach (Editor editor in m_materialEditors)
        {
            if (editor == null) continue;
            EditorGUILayout.Space(8);
            editor.DrawHeader();
            editor.OnInspectorGUI();
        }
        EditorGUILayout.EndScrollView();
    }

    private void Validate()
    {
        m_issues.Clear();
        var assets = AssetDatabase.LoadAssetAtPath<DefenseAssets>(ASSETS_PATH);
        CheckMaterial("水体材质 WaterMaterial", m_view.WaterMaterial, "Defense90/WorldWater");
        CheckMaterial("击杀特效材质 EffectMaterial", m_view.EffectMaterial, "Defense90/EffectSprite");
        CheckMaterial("技能脉冲材质 PulseMaterial", assets != null ? assets.PulseMaterial : null, "Defense90/OrbitalPulse");
        if (m_view.Timeline == null) m_issues.Add("战场预制体缺少时间轴资源 Timeline。");
        if (m_view.Skill == null || m_view.Skill.Area == null || m_view.Skill.Column == null || m_view.Skill.Splash == null)
            m_issues.Add("技能表现缺少范围/水柱/扩散渲染器，请运行 defense_author_world。");
        if (m_view.Timeline != null && (m_view.Timeline.Water.a < 1 || m_view.Timeline.Foam.a < 1 || m_view.Timeline.Outline.a < 1))
            m_issues.Add("时间轴颜色的 Alpha 应为 1，透明度由运行时进度控制。");
        if (m_full.ShaderDetail == m_simple.ShaderDetail)
            m_issues.Add("档位表两档的 Shader细节 相同，完整与简化画面将没有差别。");
    }

    private void CheckMaterial(string label, Material material, string shader)
    {
        if (material == null) { m_issues.Add(label + " 未指定。"); return; }
        if (material.shader == null || material.shader.name != shader) { m_issues.Add($"{label} 应使用 {shader}。"); return; }
        if (ShaderUtil.ShaderHasError(material.shader) || !material.shader.isSupported) m_issues.Add($"{label} 的 Shader 编译失败或当前平台不支持。");
    }

    #endregion

    private void SetActive(bool timeline)
    {
        m_hit.enabled = m_kill.enabled = m_shot.enabled = timeline;
        m_libraryWater.enabled = false;
        if (m_libraryEffect != null) m_libraryEffect.enabled = false;
    }

    private void Water(SpriteRenderer renderer, int mode, float progress, bool detail, int droplets)
    {
        Material material = renderer.sharedMaterial;
        material.SetFloat(ModeId, mode); material.SetFloat(ProgressId, progress); material.SetFloat(OpacityId, mode == 2 ? 1 - progress : 1);
        material.SetFloat(DetailId, detail ? 1 : 0); material.SetFloat(DropletsId, droplets);
        SetColors(material);
    }

    private void SetColors(Material material)
    {
        material.SetColor(WaterId, m_view.Timeline.Water); material.SetColor(FoamId, m_view.Timeline.Foam);
        material.SetColor(DeepId, m_view.Timeline.Outline);
    }

    private void Draw(Rect rect, Vector3 cameraPosition, float size, ref RenderTexture texture)
    {
        int width = Mathf.Max(1, (int)(rect.width * EditorGUIUtility.pixelsPerPoint));
        int height = Mathf.Max(1, (int)(rect.height * EditorGUIUtility.pixelsPerPoint));
        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null) DestroyImmediate(texture);
            texture = new RenderTexture(width, height, 16) { hideFlags = HideFlags.HideAndDontSave };
        }
        var camera = m_preview.camera;
        camera.transform.SetPositionAndRotation(cameraPosition, Quaternion.identity);
        camera.orthographicSize = size;
        camera.backgroundColor = m_sandGround ? s_sand : s_dark;
        camera.aspect = rect.width / rect.height;
        camera.targetTexture = texture; camera.Render(); camera.targetTexture = null;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
    }

    private static string TierName(DefenseVfxTierConfig tier) => tier.ID == DefenseConfigMgr.TIER_FULL ? "完整" : "简化";
    private static string Yes(bool value) => value ? "开" : "关";

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (m_timelineEditor != null) DestroyImmediate(m_timelineEditor);
        foreach (Editor editor in m_materialEditors) if (editor != null) DestroyImmediate(editor);
        m_materialEditors.Clear();
        foreach (var renderer in new[] { m_hit, m_kill, m_shot, m_libraryWater, m_libraryEffect })
            if (renderer != null) DestroyImmediate(renderer.sharedMaterial);
        foreach (var texture in m_thumbs.Values) if (texture != null) DestroyImmediate(texture);
        m_thumbs.Clear();
        if (m_leftTexture != null) DestroyImmediate(m_leftTexture);
        if (m_rightTexture != null) DestroyImmediate(m_rightTexture);
        m_preview?.Cleanup(); m_preview = null;
    }
}

[CustomEditor(typeof(DefenseSkillTimeline))]
public sealed class DefenseSkillTimelineEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        Field(nameof(DefenseSkillTimeline.ColumnHeight), "水柱高度");
        Field(nameof(DefenseSkillTimeline.ColumnWidth), "水柱宽度");
        Field(nameof(DefenseSkillTimeline.HitStop), "命中顿帧（秒）");
        Field(nameof(DefenseSkillTimeline.KnockDistance), "击退距离");
        Field(nameof(DefenseSkillTimeline.ShotTravel), "水弹飞行时间（秒）");
        Field(nameof(DefenseSkillTimeline.SkillDroplets), "技能弹射水滴（完整档）");
        Field(nameof(DefenseSkillTimeline.Water), "水体颜色");
        Field(nameof(DefenseSkillTimeline.Foam), "泡沫颜色");
        Field(nameof(DefenseSkillTimeline.Outline), "描边颜色");
        serializedObject.ApplyModifiedProperties();
    }

    private void Field(string name, string label) =>
        EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label));
}

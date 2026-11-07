using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace GameLogic.Defense90
{
    // 压测模式的战斗性能采样：按固定窗口统计帧耗时分位、卡顿帧、托管堆/GC 和各段 CPU 耗时。
    // 只统计 Playing 状态；窗口结束后结果停在屏幕上并输出一行日志，供真机控制台截图取证。
    public sealed class DefensePerfMonitor
    {
        public const float WINDOW_SECONDS = 60f;
        public const float JANK_MS = 33.4f;
        public static readonly string[] SEGMENTS = { "Bounds", "Rules", "Hud", "Battlefield", "FloatText" };
        private const int MAX_FRAMES = 60 * 240;
        private const float LIVE_REFRESH = 0.5f;

        private readonly float[] m_frames = new float[MAX_FRAMES];
        private readonly long[] m_marks = new long[SEGMENTS.Length + 1];
        private readonly double[] m_segmentTicks = new double[SEGMENTS.Length];
        private readonly double[] m_segmentMax = new double[SEGMENTS.Length];
        private readonly StringBuilder m_builder = new StringBuilder(512);
        private readonly Text m_text;
        private int m_count, m_jank, m_peakEnemies, m_heapDrops, m_gcStart, m_windowIndex;
        private float m_elapsed, m_liveTimer, m_frameSum, m_frameMax;
        private long m_heapStart, m_heapLast, m_heapPeak;
        private bool m_running, m_finished;

        public DefensePerfMonitor(RectTransform parent, Font font)
        {
            var go = new GameObject("PerfMonitor", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(8, 8);
            rect.sizeDelta = new Vector2(420, 260);
            m_text = go.AddComponent<Text>();
            m_text.font = font;
            m_text.fontSize = 18;
            m_text.color = Color.white;
            m_text.raycastTarget = false;
            m_text.alignment = TextAnchor.LowerLeft;
            m_text.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_text.verticalOverflow = VerticalWrapMode.Overflow;
            go.AddComponent<Outline>().effectColor = Color.black;
            m_text.text = "Perf: 等待战斗开始";
        }

        // 新回合重新开窗口；已出结果的窗口在下一局开始时清掉。
        public void Reset()
        {
            m_running = m_finished = false;
            m_text.text = "Perf: 等待战斗开始";
        }

        public void Mark(int index) => m_marks[index] = Stopwatch.GetTimestamp();

        // 在 AdvanceFrame 之后调用：delta 用未截断的 unscaledDeltaTime，包含渲染与等待，即玩家感知的帧间隔。
        public void Tick(float rawDelta, DefenseRound round)
        {
            if (m_finished || round == null || round.State != EDefenseState.Playing) return;
            if (!m_running) Begin();

            float ms = rawDelta * 1000f;
            if (m_count < MAX_FRAMES) m_frames[m_count++] = ms;
            m_frameSum += ms;
            if (ms > m_frameMax) m_frameMax = ms;
            if (ms > JANK_MS) m_jank++;
            if (round.ActiveCount > m_peakEnemies) m_peakEnemies = round.ActiveCount;

            double toMs = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < SEGMENTS.Length; i++)
            {
                double seg = (m_marks[i + 1] - m_marks[i]) * toMs;
                m_segmentTicks[i] += seg;
                if (seg > m_segmentMax[i]) m_segmentMax[i] = seg;
            }

            long heap = Profiler.GetMonoUsedSizeLong();
            if (heap < m_heapLast) m_heapDrops++;
            if (heap > m_heapPeak) m_heapPeak = heap;
            m_heapLast = heap;

            m_elapsed += rawDelta;
            if (m_elapsed >= WINDOW_SECONDS) { Finish(); return; }
            m_liveTimer -= rawDelta;
            if (m_liveTimer <= 0) { m_liveTimer = LIVE_REFRESH; ShowLive(); }
        }

        private void Begin()
        {
            m_running = true;
            m_windowIndex++;
            m_count = m_jank = m_peakEnemies = m_heapDrops = 0;
            m_elapsed = m_liveTimer = m_frameSum = m_frameMax = 0;
            Array.Clear(m_segmentTicks, 0, m_segmentTicks.Length);
            Array.Clear(m_segmentMax, 0, m_segmentMax.Length);
            m_gcStart = GC.CollectionCount(0);
            m_heapStart = m_heapLast = m_heapPeak = Profiler.GetMonoUsedSizeLong();
        }

        private void ShowLive()
        {
            float avg = m_count > 0 ? m_frameSum / m_count : 0;
            m_builder.Clear();
            m_builder.Append("Perf 采样中 ").Append(m_elapsed.ToString("0")).Append('/').Append(WINDOW_SECONDS.ToString("0")).Append("s\n");
            m_builder.Append("avg ").Append(avg.ToString("0.0")).Append("ms  >33ms ").Append(m_jank)
                .Append("  敌人峰值 ").Append(m_peakEnemies);
            m_text.text = m_builder.ToString();
        }

        private void Finish()
        {
            m_running = false;
            m_finished = true;
            int n = Mathf.Min(m_count, MAX_FRAMES);
            Array.Sort(m_frames, 0, n);
            float avg = m_frameSum / Mathf.Max(1, m_count);
            float p50 = Percentile(n, .50f), p95 = Percentile(n, .95f), p99 = Percentile(n, .99f);
            int gc = GC.CollectionCount(0) - m_gcStart;
            float heapGrowMb = (m_heapLast - m_heapStart) / 1048576f;
            float heapPeakMb = m_heapPeak / 1048576f;

            m_builder.Clear();
            m_builder.Append("Perf #").Append(m_windowIndex).Append(' ').Append(WINDOW_SECONDS.ToString("0")).Append("s ")
                .Append(Application.platform).Append(' ').Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" target=").Append(Application.targetFrameRate).Append(" tier=").Append(DefenseSettings.VfxTier).Append('\n');
            m_builder.Append("frames ").Append(m_count).Append(" avg ").Append(avg.ToString("0.0"))
                .Append(" p50 ").Append(p50.ToString("0.0")).Append(" p95 ").Append(p95.ToString("0.0"))
                .Append(" p99 ").Append(p99.ToString("0.0")).Append(" max ").Append(m_frameMax.ToString("0.0")).Append("ms\n");
            m_builder.Append(">33ms ").Append(m_jank).Append(" (").Append((100f * m_jank / Mathf.Max(1, m_count)).ToString("0.0"))
                .Append("%)  敌人峰值 ").Append(m_peakEnemies).Append('\n');
            m_builder.Append("heap +").Append(heapGrowMb.ToString("0.00")).Append("MB peak ").Append(heapPeakMb.ToString("0.0"))
                .Append("MB GC ").Append(gc).Append(" heapDrops ").Append(m_heapDrops).Append('\n');
            m_builder.Append("CPU avg/max ms:");
            for (int i = 0; i < SEGMENTS.Length; i++)
                m_builder.Append(i % 2 == 0 ? "\n " : "  ").Append(SEGMENTS[i]).Append(' ')
                    .Append((m_segmentTicks[i] / Mathf.Max(1, m_count)).ToString("0.00")).Append('/')
                    .Append(m_segmentMax[i].ToString("0.0"));

            string result = m_builder.ToString();
            m_text.text = result;
            Debug.Log("[Defense90.Perf] " + result.Replace('\n', '|'));
        }

        private float Percentile(int n, float p)
        {
            if (n == 0) return 0;
            int index = Mathf.Clamp(Mathf.CeilToInt(p * n) - 1, 0, n - 1);
            return m_frames[index];
        }
    }
}

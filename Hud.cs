using System;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// IMGUI overlay for the local player. Percent on the right with the Smash
    /// shake, bubble readiness icon on the left with pip dots and a cooldown timer.
    /// </summary>
    internal static class Hud
    {
        private static Font      _font;
        private static bool      _fontSearched;
        private static Texture2D _bubbleTex;
        private static Texture2D _dotTex;
        private static GUIStyle  _percentStyle;
        private static GUIStyle  _timerStyle;
        private static GUIStyle  _labelStyle;

        private static double _shakeStart = double.MinValue;
        private static float  _shakeSeed;

        internal static void Init()
        {
            ShieldState.PercentIncreased -= OnPercentIncreased; // never subscribe twice on a reload
            ShieldState.PercentIncreased += OnPercentIncreased;
        }

        /// <summary>
        /// PercentIncreased is a static event, so it outlives the plugin object on a
        /// reload; without this the old handler stays wired to a dead HUD forever.
        /// </summary>
        internal static void Shutdown()
        {
            ShieldState.PercentIncreased -= OnPercentIncreased;
            if (_panelTex  != null) { UnityEngine.Object.Destroy(_panelTex);  _panelTex  = null; }
            if (_bubbleTex != null) { UnityEngine.Object.Destroy(_bubbleTex); _bubbleTex = null; }
            if (_dotTex    != null) { UnityEngine.Object.Destroy(_dotTex);    _dotTex    = null; }
            _percentStyle = null;
            _fontSearched = false;
            _cachedPct = int.MinValue;
        }

        /// <summary>Font name edited in game: look it up again on the next draw.</summary>
        internal static void InvalidateFont()
        {
            _fontSearched = false;
            _percentStyle = null;
            _cachedPct = int.MinValue;
        }

        // Percent text and its measurements only change when the percent does.
        private static int _cachedPct = int.MinValue;
        private static int _cachedFontSize = -1;
        private static readonly GUIContent _pctContent = new GUIContent();
        private static Vector2 _pctSize, _pctMax;

        private static void OnPercentIncreased(float _)
        {
            _shakeStart = Time.timeAsDouble;
            _shakeSeed  = UnityEngine.Random.value * 100f;
        }

        // ---- Assets ----------------------------------------------------------

        private static void EnsureAssets()
        {
            if (!_fontSearched)
            {
                _fontSearched = true;
                _font = FindFont(Plugin.PercentFontName.Value);
                if (_font == null)
                    Plugin.Log.LogWarning($"Font '{Plugin.PercentFontName.Value}' not installed on this machine; using the default. " +
                                          "Install DF Gothic as a system font and restart to use it.");
                else
                    Plugin.Log.LogInfo($"Percent font: {_font.name}");
            }

            if (_bubbleTex == null) _bubbleTex = MakeBubbleTexture(128);
            if (_dotTex == null)    _dotTex    = MakeDotTexture(24);

            if (_percentStyle == null)
            {
                _percentStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerLeft,
                    fontStyle = FontStyle.Bold,
                    wordWrap  = false,
                    clipping  = TextClipping.Overflow,
                };
                if (_font != null) _percentStyle.font = _font;
                _percentStyle.normal.textColor = Color.white;

                _timerStyle = new GUIStyle(_percentStyle) { alignment = TextAnchor.MiddleCenter };
                _labelStyle = new GUIStyle(_percentStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal };
            }
        }

        private static Font FindFont(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return null;
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                string norm = Normalize(wanted);
                string best = null;
                foreach (var name in installed)
                {
                    string n = Normalize(name);
                    if (n == norm) { best = name; break; }
                    if (best == null && n.Contains(norm)) best = name;
                }
                if (best == null) return null;
                return Font.CreateDynamicFontFromOSFont(best, 64);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Font lookup failed: " + e.Message);
                return null;
            }
        }

        private static string Normalize(string s) => s.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

        private static Texture2D MakeBubbleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (d < 1f)
                {
                    // Body: soft fill that thickens toward the rim, like a real bubble.
                    float body = Mathf.Lerp(0.22f, 0.55f, Mathf.Pow(d, 2.5f));
                    // Rim: bright ring just inside the edge.
                    float rim  = Mathf.Exp(-Mathf.Pow((d - 0.93f) / 0.05f, 2f)) * 0.9f;
                    // Highlight: top-left glint.
                    float hx = dx + 0.42f, hy = dy - 0.42f;
                    float hl = Mathf.Exp(-(hx * hx + hy * hy) / 0.05f) * 0.85f;
                    a = Mathf.Clamp01(body + rim + hl);
                    // Antialias the edge.
                    a *= Mathf.Clamp01((1f - d) / 0.03f);
                }
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeDotTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / 0.12f));
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ---- Drawing ---------------------------------------------------------

        internal static void Draw()
        {
            // IMGUI calls OnGUI several times a frame (layout, repaint, input); only repaint draws.
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!Plugin.ShowHud.Value) return;
            if (!ModHandshake.GameplayEnabled) { DrawStandDownNotice(); return; }
            var player = GameManager.LocalPlayerInfo;
            if (player == null || !ShouldShow()) return;
            try { if (player.Movement != null && player.Movement.IsRespawningOrDrowning) return; } catch { }
            if (KillZone.IsLingering) return; // dead and gone; nothing to show

            EnsureAssets();

            float scale = Plugin.HudScale.Value;
            float bottom = Screen.height - Plugin.HudBottomMargin.Value * scale;

            // No percent outside a hole: the range and the lobby are practice space,
            // so the shield works but the number is meaningless there.
            bool showPercent = ShieldState.InPlayableHole;

            // Percent bottom centre, measured at its widest (the shake punch) so the
            // icon to its right never overlaps, even at 300%.
            int fontSize = Mathf.RoundToInt(Plugin.PercentFontSize.Value * scale);
            int pct = Mathf.RoundToInt(ShieldState.Percent);
            if (pct != _cachedPct || fontSize != _cachedFontSize)
            {
                _cachedPct = pct; _cachedFontSize = fontSize;
                _pctContent.text = pct + "%";
                _percentStyle.fontSize = Mathf.RoundToInt(fontSize * (1f + Plugin.PercentShakePunch.Value));
                _pctMax = _percentStyle.CalcSize(_pctContent);
                _percentStyle.fontSize = fontSize;
                _pctSize = _percentStyle.CalcSize(_pctContent);
            }
            float left = showPercent
                ? (Screen.width - _pctSize.x) * 0.5f + Plugin.HudHorizontalOffset.Value * scale
                : Screen.width * 0.5f + Plugin.HudHorizontalOffset.Value * scale;   // icon alone: centre it
            // Digits sit above their own baseline, so a bottom-aligned number reads high
            // against a circle. Nudge it down onto the icon's line.
            float pctBottom = bottom + Plugin.PercentVerticalOffset.Value * scale;
            if (showPercent)
                DrawPercent(new Rect(left, pctBottom - _pctSize.y, _pctSize.x, _pctSize.y), _pctContent.text, fontSize);

            // Bubble readiness icon, bottom-aligned with the percent. Shown when the
            // shield is up or could be raised; when it cannot (cart, knocked out,
            // respawning, diving, swinging) only a running break cooldown keeps it on
            // screen, counting down, and pips are hidden during the cooldown.
            bool up = player.IsElectromagnetShieldActive;
            bool onBreakCd = ShieldState.IsOnBreakCooldown || (ShieldState.Pips <= 0 && !Plugin.RestoreAfterBreakCooldown.Value);
            bool blocked = !up && Plugin.ActivationBlockedByState(player, true);
            bool showIcon = up || !blocked || onBreakCd;
            bool showPips = Plugin.ShowPipDots.Value && showIcon && !onBreakCd && ShieldState.Pips > 0;
            if (showPips && !_pipsWereShown) _pipsShownAt = Time.timeAsDouble;
            _pipsWereShown = showPips;
            if (!showIcon) return;

            float bubbleSize = Plugin.BubbleHudSize.Value * scale;
            float gap = Plugin.BubbleHudGap.Value * scale;
            float bx = !showPercent ? left - bubbleSize * 0.5f
                     : Plugin.BubbleHudOnLeft.Value ? left - gap - bubbleSize
                     : left + _pctMax.x + gap;
            float by = bottom - bubbleSize; // pips hang below the baseline; the margin default leaves room
            DrawBubble(player, new Rect(bx, by, bubbleSize, bubbleSize), scale, up, onBreakCd, showPips);
        }

        private static bool   _pipsWereShown;
        private static double _pipsShownAt = double.MinValue;

        /// <summary>Ease-out-back: overshoots a little, like a pop.</summary>
        private static float PopEase(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static bool ShouldShow()
        {
            try
            {
                if (PauseMenu.IsPaused) return false;
                if (Scoreboard.IsVisible) return false;
                if (ShieldState.InDrivingRange) return true; // shield is testable there
                switch (CourseManager.MatchState)
                {
                    case MatchState.TeeOff:
                    case MatchState.Ongoing:
                    case MatchState.CountingDownToEnd:
                    case MatchState.Overtime:
                        return true;
                    default:
                        return false;
                }
            }
            catch { return true; }
        }

        private static void DrawPercent(Rect rect, string text, int fontSize)
        {
            // ONE shake, not two. It fires on a hit and decays to nothing, so the number
            // sits still when you are not being hit. What percent controls is how LONG
            // it takes to settle: a graze at 10% is over in a moment, a hit at 90% keeps
            // rattling for seconds. The motion is a rumble -- small, fast, in every
            // direction -- rather than a vertical bounce, which read as a bug at size.
            float dur = ShakeDuration(ShieldState.Percent);
            float t = dur <= 0.01f ? 1f : (float)((Time.timeAsDouble - _shakeStart) / dur);

            float amp = 0f, punch = 1f;
            if (t >= 0f && t < 1f)
            {
                float decay = (1f - t) * (1f - t);      // fast at first, long calm tail
                amp   = Plugin.PercentShakePixels.Value * decay;
                punch = 1f + Plugin.PercentShakePunch.Value * decay * decay;
            }

            Vector2 offset = Vector2.zero;
            if (amp > 0.01f)
            {
                float n = Time.time * Plugin.PercentShakeSpeed.Value + _shakeSeed;
                offset = new Vector2(
                    (Mathf.PerlinNoise(n, 0.3f) - 0.5f) * 2f * amp,
                    (Mathf.PerlinNoise(0.7f, n) - 0.5f) * 2f * amp);
            }

            var r = new Rect(rect.x + offset.x, rect.yMax - _pctSize.y + offset.y, _pctSize.x, _pctSize.y);

            Color c = PercentColor(ShieldState.Percent);
            float o = Mathf.Max(1f, 3f * Plugin.HudScale.Value);

            Matrix4x4 saved = GUI.matrix;
            if (punch != 1f) GUIUtility.ScaleAroundPivot(new Vector2(punch, punch), r.center);
            _percentStyle.fontSize = fontSize;

            // Motion blur: the same text redrawn a few times along the direction it is
            // moving, each fainter than the last. IMGUI has no real blur, but smearing
            // copies along the travel vector reads as one at this size and speed.
            int trails = Mathf.RoundToInt(Plugin.PercentBlurSamples.Value);
            if (trails > 0 && amp > 0.5f)
            {
                Vector2 travel = offset - _lastOffset;
                if (travel.sqrMagnitude > 0.01f)
                {
                    float strength = Mathf.Clamp01(amp / Mathf.Max(1f, Plugin.PercentShakePixels.Value));
                    for (int i = 1; i <= trails; i++)
                    {
                        float f = i / (float)(trails + 1);
                        var ghost = new Rect(r.x - travel.x * f * Plugin.PercentBlurLength.Value,
                                             r.y - travel.y * f * Plugin.PercentBlurLength.Value,
                                             r.width, r.height);
                        var gc = c; gc.a = c.a * (1f - f) * 0.5f * strength;
                        var prev = GUI.color; GUI.color = gc;
                        GUI.Label(ghost, text, _percentStyle);
                        GUI.color = prev;
                    }
                }
            }
            _lastOffset = offset;

            DrawOutlined(r, text, _percentStyle, c, Color.black, o);
            GUI.matrix = saved;
        }

        private static Vector2 _lastOffset;

        /// <summary>
        /// Percent to settle time. Thresholds, not a straight line: the config string
        /// is "percent:seconds" pairs, and anything between two thresholds interpolates.
        /// Default "0:0.8, 30:2, 60:3, 90:5" -- so 45% lands at 2.5s.
        /// </summary>
        private static float ShakeDuration(float pct)
        {
            var table = ShakeTable();
            if (table == null || table.Length == 0) return Plugin.PercentShakeDuration.Value;
            if (pct <= table[0].pct) return table[0].secs;
            for (int i = 1; i < table.Length; i++)
            {
                if (pct > table[i].pct) continue;
                var a = table[i - 1]; var b = table[i];
                float span = Mathf.Max(0.001f, b.pct - a.pct);
                return Mathf.Lerp(a.secs, b.secs, (pct - a.pct) / span);
            }
            return table[table.Length - 1].secs;
        }

        private struct ShakeStep { public float pct, secs; }
        private static ShakeStep[] _shakeTable;
        private static string _shakeTableSource;

        internal static void InvalidateShakeTable() { _shakeTable = null; }

        private static ShakeStep[] ShakeTable()
        {
            string src = Plugin.PercentShakeDurationTable.Value;
            if (_shakeTable != null && src == _shakeTableSource) return _shakeTable;
            _shakeTableSource = src;

            var list = new System.Collections.Generic.List<ShakeStep>();
            foreach (var part in src.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                if (!float.TryParse(kv[0].Trim(), out float p)) continue;
                if (!float.TryParse(kv[1].Trim(), out float sec)) continue;
                list.Add(new ShakeStep { pct = p, secs = Mathf.Max(0f, sec) });
            }
            list.Sort((x, y) => x.pct.CompareTo(y.pct));
            if (list.Count == 0)
                Plugin.Log.LogWarning($"Could not read PercentShakeDurationTable '{src}'; using PercentShakeDuration instead.");
            _shakeTable = list.ToArray();
            return _shakeTable;
        }

        private static GUIStyle _noticeBig, _noticeSmall;
        private static Texture2D _panelTex;

        /// <summary>
        /// The gate is closed, so the normal HUD would be a lie. For the first few
        /// seconds this is a panel you cannot miss, naming the player and both
        /// versions; after that it shrinks to one line so it stops covering the game.
        /// </summary>
        private static void DrawStandDownNotice()
        {
            string why = ModHandshake.BlockReason;
            if (string.IsNullOrEmpty(why)) return;

            if (_noticeBig == null)
            {
                _noticeBig = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
                _noticeSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }
            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _panelTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
                _panelTex.Apply();
            }

            float scale = Plugin.HudScale.Value;
            bool loud = Time.timeAsDouble - ModHandshake.BlockedSince < Plugin.MismatchPopupDuration.Value;

            if (!loud)
            {
                _noticeSmall.fontSize = Mathf.RoundToInt(16f * scale);
                string line = "SBG Shields inactive - " + why;
                var s2 = _noticeSmall.CalcSize(new GUIContent(line));
                var r2 = new Rect((Screen.width - s2.x) * 0.5f, 8f * scale, s2.x, s2.y);
                DrawOutlined(r2, line, _noticeSmall, new Color(1f, 0.78f, 0.25f), Color.black, 2f);
                return;
            }

            _noticeBig.fontSize   = Mathf.RoundToInt(26f * scale);
            _noticeSmall.fontSize = Mathf.RoundToInt(17f * scale);

            float w = Mathf.Min(Screen.width * 0.7f, 720f * scale);
            float h = 150f * scale;
            var panel = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.22f, w, h);
            GUI.DrawTexture(panel, _panelTex, ScaleMode.StretchToFill);

            var title = new Rect(panel.x, panel.y + 14f * scale, panel.width, 34f * scale);
            DrawOutlined(title, "SBG Shields is standing down", _noticeBig, new Color(1f, 0.78f, 0.25f), Color.black, 2f);

            var body = new Rect(panel.x + 20f * scale, panel.y + 54f * scale, panel.width - 40f * scale, 80f * scale);
            bool publicLobby = why.Contains("public lobby");
            string text = publicLobby
                ? "The shield is disabled in public lobbies on purpose.\n\nHost a Friends or Invite Only lobby to use it."
                : why + ".\n\nThe match is running vanilla rules so nothing desyncs. " +
                  "Everyone needs SBG Shields " + Plugin.Version + " for the shield to work.";
            DrawOutlined(body, text, _noticeSmall, Color.white, Color.black, 1.5f);
        }

        private static Color PercentColor(float pct)
        {
            float t = Mathf.Clamp01(pct / Mathf.Max(1f, Plugin.PercentForMaxScaling.Value));
            // white -> yellow -> orange -> red, roughly Smash.
            if (t < 0.33f) return Color.Lerp(Color.white, new Color(1f, 0.92f, 0.25f), t / 0.33f);
            if (t < 0.66f) return Color.Lerp(new Color(1f, 0.92f, 0.25f), new Color(1f, 0.55f, 0.1f), (t - 0.33f) / 0.33f);
            return Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(0.9f, 0.08f, 0.08f), (t - 0.66f) / 0.34f);
        }

        private static void DrawBubble(PlayerInfo player, Rect rect, float scale, bool up, bool onBreakCd, bool showPips)
        {
            Color skin = Skin.Of(player);
            bool ready = ShieldState.CanActivate(out _);
            float wait = ShieldState.SecondsUntilReady;

            Color tint = skin;
            if (up)
            {
                tint = Color.Lerp(skin, Color.white, 0.25f);
                rect = Grow(rect, 6f * scale);
            }
            else if (!ready)
            {
                tint = skin * 0.3f;
                tint.a = 1f;
            }

            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, _bubbleTex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;

            // Cooldown timer over the darkened bubble.
            if (!ready)
            {
                _timerStyle.fontSize = Mathf.RoundToInt(Plugin.PercentFontSize.Value * 0.42f * scale);
                string txt = float.IsInfinity(wait) ? "X" : (wait >= 10f ? $"{wait:0}" : $"{wait:0.0}");
                DrawOutlined(rect, txt, _timerStyle, Color.white, Color.black, Mathf.Max(1f, 2f * scale));
            }

            if (!showPips) { GUI.color = prev; return; }

            // Pip dots under the bubble, popping in left to right when they reappear.
            int max = Mathf.Max(1, Plugin.MaxPips.Value);
            float dot = 12f * scale, dgap = 5f * scale;
            float rowW = max * dot + (max - 1) * dgap;
            float dx = rect.center.x - rowW * 0.5f;
            float dy = rect.yMax + 4f * scale;
            double since = Time.timeAsDouble - _pipsShownAt;
            for (int i = 0; i < max; i++)
            {
                bool filled = i < ShieldState.Pips;
                float pop = PopEase((float)((since - i * 0.06) / 0.16));
                if (pop <= 0f) continue;
                float d = dot * pop;
                float cx = dx + i * (dot + dgap) + dot * 0.5f, cy = dy + dot * 0.5f;
                GUI.color = filled ? skin : new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(cx - d * 0.5f, cy - d * 0.5f, d, d), _dotTex, ScaleMode.ScaleToFit, true);
            }
            GUI.color = prev;
        }

        private static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + 2 * by, r.height + 2 * by);

        private static void DrawOutlined(Rect r, string text, GUIStyle style, Color fill, Color outline, float thickness)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = outline;
            GUI.Label(new Rect(r.x - thickness, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + thickness, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y - thickness, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + thickness, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + thickness, r.y + thickness, r.width, r.height), text, style);
            style.normal.textColor = fill;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }
    }
}

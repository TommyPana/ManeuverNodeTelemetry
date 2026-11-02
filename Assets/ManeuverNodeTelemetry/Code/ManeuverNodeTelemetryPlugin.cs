using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using Redux.ExtraModTypes;
using SpaceWarp2.API.Mods;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverNodeTelemetry
{
    public enum PageType
    {
        OrbitSubOrbital,
        SurfaceDynamics,
        PropulsionDeltaV,
        TargetTracking,
        ManeuverNode,
        CustomUser1,
        CustomUser2
    }

    public enum TelemetryMetric
    {
        Apoapsis,
        Periapsis,
        TimeToAp,
        TimeToPe,
        Inclination,
        Eccentricity,
        OrbitPeriod,
        ManeuverBurnDv,
        ManeuverBurnDuration,
        ManeuverApoapsis,
        ManeuverPeriapsis,
        ManeuverTimeToAp,
        ManeuverTimeToPe,
        AltTerrain,
        AltSeaLevel,
        VertSpeed,
        HorizSpeed,
        SurfaceSpeed,
        Situation,
        Biome,
        TotalDeltaV,
        StageDeltaV,
        TargetName,
        TargetDistance,
        TargetRelSpeed
    }

    public enum ColorTarget
    {
        Accent,
        Labels,
        Values,
        Logos
    }

    public class ManeuverNodeTelemetryPlugin : KerbalMod
    {
        private UIDocument _uiDocument;
        private VisualElement _appBarRoot;

        // 1. Primary Telemetry Window
        private VisualElement _windowRoot;
        private VisualElement _winHeaderLine;
        private bool _isWindowVisible = true;
        private PageType _activePage = PageType.ManeuverNode;
        private int _activeSlotIndex = 2;

        // 2. Independent Settings Window & Paging
        private VisualElement _settingsRoot;
        private VisualElement _settingsHeaderLine;
        private bool _isSettingsVisible = false;
        private int _settingsSubPage = 0;
        private PageType _settingsEditingPage = PageType.ManeuverNode;
        private Label _settingsSubPageTitle;

        // Default Baseline Values for Resetting
        private const float DefaultFontSize = 9.0f;
        private const float DefaultLogoSize = 16.0f; // 16px baseline for 16x16 pixel art
        private const float DefaultTransparency = 0.90f;
        private static readonly Color DefaultAccent = new Color(0.54f, 0.29f, 1.0f, 1f); // KSP Purple
        private static readonly Color DefaultLabels = new Color(0.51f, 0.61f, 0.71f, 1f); // Muted Blue
        private static readonly Color DefaultValues = new Color(0.0f, 0.95f, 0.45f, 1f); // Mint Green
        private static readonly Color DefaultLogos = new Color(0.92f, 0.96f, 1f, 1f);  // White

        // Live Appearance Values
        private float _customFontSize = DefaultFontSize;
        private float _customLogoSize = DefaultLogoSize;
        private float _boxTransparency = DefaultTransparency;
        private Color _accentColor = DefaultAccent;
        private Color _labelColor = DefaultLabels;
        private Color _valueColor = DefaultValues;
        private Color _logoColor = DefaultLogos;

        // HSV Editor State
        private ColorTarget _selectedColorTarget = ColorTarget.Accent;

        // Slot Assignments for Buttons 1, 2, 3
        private PageType[] _slotAssignments = new PageType[]
        {
            PageType.OrbitSubOrbital,
            PageType.SurfaceDynamics,
            PageType.ManeuverNode
        };

        // Per-Page Metrics & Default Factory
        private Dictionary<PageType, List<TelemetryMetric>> _pageMetrics = new Dictionary<PageType, List<TelemetryMetric>>();

        // Texture Cache for Pixel Art Icons
        private Dictionary<string, Texture2D> _iconTextures = new Dictionary<string, Texture2D>();
        private VisualElement[] _slotIconElements = new VisualElement[4];
        private VisualElement[] _slotPips = new VisualElement[4];

        // Content Area Elements
        private Label _windowTitle;
        private ScrollView _contentScrollView;
        private ScrollView _settingsScrollView;
        private Dictionary<string, Label> _dynamicLabels = new Dictionary<string, Label>();
        private List<Label> _rowTitleLabels = new List<Label>();
        private List<Label> _rowValueLabels = new List<Label>();

        // Dragging & Resizing States
        private bool _isBarDragging, _isWinDragging, _isSettingsDragging;
        private bool _isWinResizing, _isSettingsResizing;
        private Vector2 _barDragOffset, _winDragOffset, _settingsDragOffset;
        private Vector3 _initialWinPointerPos, _initialSettingsPointerPos;
        private Vector2 _initialWinSize, _initialSettingsSize;

        private const float MinWidth = 260f;
        private const float MinHeight = 160f;
        private const float SettingsMinWidth = 320f;
        private const float SettingsMinHeight = 300f;

        private const float DefaultWinWidth = 320f;
        private const float DefaultWinHeight = 280f;
        private const float DefaultSettingsWidth = 390f;
        private const float DefaultSettingsHeight = 560f;

        private Font _kspMonospaceFont;

        public override void OnInitialized()
        {
            base.OnInitialized();
            SWLogger.LogInfo($"{SWMetadata.Name} initialized on branch version-0.05.");

            ResolveGameFonts();
            LoadPixelArtIcons();
            InitializeAllPagesToDefaults();
            MountUI();
        }

        private void ResolveGameFonts()
        {
            try
            {
                Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                foreach (var f in fonts)
                {
                    if (f.name.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.name.IndexOf("agency", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.name.IndexOf("consolas", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _kspMonospaceFont = f;
                        break;
                    }
                }
            }
            catch
            {
                _kspMonospaceFont = null;
            }
        }

        // ================= TEXTURE LOADER =================
        private void LoadPixelArtIcons()
        {
            string modFolder = string.Empty;

            try
            {
                // In SpaceWarp, SWMetadata.Folder is a DirectoryInfo object
                if (SWMetadata != null && SWMetadata.Folder != null)
                {
                    modFolder = SWMetadata.Folder.FullName;
                }
            }
            catch
            {
                modFolder = string.Empty;
            }

            // Universal fallback without BepInEx namespace dependencies
            if (string.IsNullOrEmpty(modFolder))
            {
                string gamePlugins = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "ManeuverNodeTelemetry");
                modFolder = Path.GetFullPath(gamePlugins);
            }

            // Test multiple common nesting variations created by ThunderKit and manual copies
            string[] possiblePaths = new string[]
            {
                Path.Combine(modFolder, "Assets", "Images"),
                Path.Combine(modFolder, "Assets", "images"),
                Path.Combine(modFolder, "images"),
                Path.Combine(modFolder, "Images"),
                Path.Combine(modFolder, "Assets", "ManeuverNodeTelemetry", "Assets", "Images"),
                Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "ManeuverNodeTelemetry", "Assets", "Images")
            };

            string imagesDir = string.Empty;
            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    imagesDir = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(imagesDir))
            {
                SWLogger.LogError($"[MNT] Could not locate icon folder! Looked in:\n - {string.Join("\n - ", possiblePaths)}");
                return;
            }

            SWLogger.LogInfo($"[MNT] Successfully located icons folder at: {imagesDir}");

            LoadSingleIcon("orbit", Path.Combine(imagesDir, "icon_orbit.png"));
            LoadSingleIcon("surface", Path.Combine(imagesDir, "icon_surface.png"));
            LoadSingleIcon("propulsion", Path.Combine(imagesDir, "icon_propulsion.png"));
            LoadSingleIcon("target", Path.Combine(imagesDir, "icon_target.png"));
            LoadSingleIcon("maneuver", Path.Combine(imagesDir, "icon_maneuver.png"));
            LoadSingleIcon("user1", Path.Combine(imagesDir, "icon_user1.png"));
            LoadSingleIcon("user2", Path.Combine(imagesDir, "icon_user2.png"));
            LoadSingleIcon("settings", Path.Combine(imagesDir, "icon_settings.png"));
        }

        private void LoadSingleIcon(string key, string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                SWLogger.LogWarning($"Icon not found at: {fullPath}");
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point; // Nearest-neighbor preserves crisp pixel art
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.LoadImage(bytes);
                _iconTextures[key] = tex;
                SWLogger.LogInfo($"Successfully loaded icon: {key}");
            }
            catch (Exception ex)
            {
                SWLogger.LogError($"Error loading texture '{key}': {ex.Message}");
            }
        }

        private Texture2D GetTextureForPage(PageType page)
        {
            string key;
            switch (page)
            {
                case PageType.OrbitSubOrbital: key = "orbit"; break;
                case PageType.SurfaceDynamics: key = "surface"; break;
                case PageType.PropulsionDeltaV: key = "propulsion"; break;
                case PageType.TargetTracking: key = "target"; break;
                case PageType.ManeuverNode: key = "maneuver"; break;
                case PageType.CustomUser1: key = "user1"; break;
                case PageType.CustomUser2: key = "user2"; break;
                default: return null;
            }

            _iconTextures.TryGetValue(key, out Texture2D tex);
            return tex;
        }

        private void ApplyPixelStyle(VisualElement el, float fontSize = 9f, bool isBold = false)
        {
            if (_kspMonospaceFont != null) el.style.unityFont = _kspMonospaceFont;
            el.style.fontSize = fontSize;
            el.style.unityFontStyleAndWeight = isBold ? FontStyle.Bold : FontStyle.Normal;
            el.style.letterSpacing = 0.5f;
        }

        // ================= DEFAULTS AND PRESETS =================
        private List<TelemetryMetric> GetDefaultMetricsForPage(PageType page)
        {
            switch (page)
            {
                case PageType.ManeuverNode:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.ManeuverBurnDv,
                        TelemetryMetric.ManeuverBurnDuration,
                        TelemetryMetric.ManeuverApoapsis,
                        TelemetryMetric.ManeuverTimeToAp,
                        TelemetryMetric.ManeuverPeriapsis,
                        TelemetryMetric.ManeuverTimeToPe,
                        TelemetryMetric.Apoapsis,
                        TelemetryMetric.Periapsis
                    };
                case PageType.OrbitSubOrbital:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.OrbitPeriod,
                        TelemetryMetric.Apoapsis,
                        TelemetryMetric.TimeToAp,
                        TelemetryMetric.Periapsis,
                        TelemetryMetric.TimeToPe,
                        TelemetryMetric.Inclination,
                        TelemetryMetric.Eccentricity
                    };
                case PageType.SurfaceDynamics:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.AltTerrain,
                        TelemetryMetric.AltSeaLevel,
                        TelemetryMetric.VertSpeed,
                        TelemetryMetric.HorizSpeed,
                        TelemetryMetric.SurfaceSpeed,
                        TelemetryMetric.Situation,
                        TelemetryMetric.Biome
                    };
                case PageType.PropulsionDeltaV:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.TotalDeltaV,
                        TelemetryMetric.StageDeltaV
                    };
                case PageType.TargetTracking:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.TargetName,
                        TelemetryMetric.TargetDistance,
                        TelemetryMetric.TargetRelSpeed
                    };
                case PageType.CustomUser1:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.AltTerrain,
                        TelemetryMetric.VertSpeed,
                        TelemetryMetric.TotalDeltaV,
                        TelemetryMetric.Situation
                    };
                case PageType.CustomUser2:
                    return new List<TelemetryMetric>
                    {
                        TelemetryMetric.TargetName,
                        TelemetryMetric.TargetDistance,
                        TelemetryMetric.TargetRelSpeed,
                        TelemetryMetric.Inclination
                    };
                default:
                    return new List<TelemetryMetric> { TelemetryMetric.Apoapsis, TelemetryMetric.Periapsis };
            }
        }

        private void InitializeAllPagesToDefaults()
        {
            foreach (PageType p in Enum.GetValues(typeof(PageType)))
            {
                _pageMetrics[p] = GetDefaultMetricsForPage(p);
            }
        }

        private void ResetAppearanceToDefaults()
        {
            _customFontSize = DefaultFontSize;
            _customLogoSize = DefaultLogoSize;
            _boxTransparency = DefaultTransparency;
            _accentColor = DefaultAccent;
            _labelColor = DefaultLabels;
            _valueColor = DefaultValues;
            _logoColor = DefaultLogos;

            ApplyVisualTheme();
            RebuildSettingsView();
        }

        private void MountUI()
        {
            GameObject uiGo = new GameObject("ManeuverNodeTelemetry_UIDoc");
            UnityEngine.Object.DontDestroyOnLoad(uiGo);
            _uiDocument = uiGo.AddComponent<UIDocument>();

            PanelSettings panelSettings = Resources.Load<PanelSettings>("KerbalPanelSettings");
            if (panelSettings == null)
            {
                PanelSettings[] all = Resources.FindObjectsOfTypeAll<PanelSettings>();
                if (all != null && all.Length > 0) panelSettings = all[0];
            }
            if (panelSettings != null) _uiDocument.panelSettings = panelSettings;

            _uiDocument.rootVisualElement.Clear();
            BuildAppBar(_uiDocument.rootVisualElement);
            BuildMainWindow(_uiDocument.rootVisualElement);
            BuildSettingsWindow(_uiDocument.rootVisualElement);

            ApplyVisualTheme();
            OpenTelemetrySlot(2);
        }

        // ================= APP BAR =================
        private void BuildAppBar(VisualElement root)
        {
            _appBarRoot = new VisualElement();
            _appBarRoot.name = "app-bar-root";
            _appBarRoot.pickingMode = PickingMode.Position;
            _appBarRoot.style.position = UnityEngine.UIElements.Position.Absolute;
            _appBarRoot.style.top = 35;
            _appBarRoot.style.left = 300;
            _appBarRoot.style.flexDirection = FlexDirection.Row;
            _appBarRoot.style.alignItems = Align.Center;
            _appBarRoot.style.borderLeftWidth = 1;
            _appBarRoot.style.borderRightWidth = 1;
            _appBarRoot.style.borderTopWidth = 1;
            _appBarRoot.style.borderBottomWidth = 1;
            _appBarRoot.style.borderTopLeftRadius = 4;
            _appBarRoot.style.borderTopRightRadius = 4;
            _appBarRoot.style.borderBottomLeftRadius = 4;
            _appBarRoot.style.borderBottomRightRadius = 4;
            _appBarRoot.style.paddingLeft = 3;
            _appBarRoot.style.paddingRight = 3;
            _appBarRoot.style.paddingTop = 3;
            _appBarRoot.style.paddingBottom = 3;

            VisualElement barGrip = new VisualElement();
            barGrip.style.width = 10;
            barGrip.style.height = 32;
            barGrip.style.marginRight = 3;
            barGrip.style.borderLeftWidth = 2;
            barGrip.style.borderRightWidth = 2;
            barGrip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _isBarDragging = true;
                    _barDragOffset = evt.localPosition;
                    barGrip.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            barGrip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isBarDragging && barGrip.HasPointerCapture(evt.pointerId))
                {
                    Vector2 pPos = _appBarRoot.parent != null ? _appBarRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _appBarRoot.style.left = pPos.x - _barDragOffset.x;
                    _appBarRoot.style.top = pPos.y - _barDragOffset.y;
                    evt.StopPropagation();
                }
            });
            barGrip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isBarDragging && evt.button == 0)
                {
                    _isBarDragging = false;
                    barGrip.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            _appBarRoot.Add(barGrip);

            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i;
                Button btnSlot = new Button(() => OnSlotClicked(slotIndex));
                btnSlot.style.width = 46;
                btnSlot.style.height = 34;
                btnSlot.style.marginLeft = 2;
                btnSlot.style.marginRight = 2;
                btnSlot.style.paddingTop = 2;
                btnSlot.style.paddingBottom = 2;
                btnSlot.style.paddingLeft = 0;
                btnSlot.style.paddingRight = 0;
                btnSlot.style.backgroundColor = new Color(0.08f, 0.12f, 0.18f, 0.9f);
                btnSlot.style.borderLeftWidth = 1;
                btnSlot.style.borderRightWidth = 1;
                btnSlot.style.borderTopWidth = 1;
                btnSlot.style.borderBottomWidth = 1;
                btnSlot.style.borderLeftColor = new Color(0.18f, 0.26f, 0.36f);
                btnSlot.style.borderRightColor = new Color(0.18f, 0.26f, 0.36f);
                btnSlot.style.borderTopColor = new Color(0.18f, 0.26f, 0.36f);
                btnSlot.style.borderBottomColor = new Color(0.18f, 0.26f, 0.36f);
                btnSlot.style.alignItems = Align.Center;
                btnSlot.style.justifyContent = Justify.SpaceBetween;

                VisualElement pip = new VisualElement();
                pip.pickingMode = PickingMode.Ignore;
                pip.style.width = 18;
                pip.style.height = 3;
                pip.style.borderTopLeftRadius = 2;
                pip.style.borderTopRightRadius = 2;
                pip.style.borderBottomLeftRadius = 2;
                pip.style.borderBottomRightRadius = 2;
                pip.style.backgroundColor = new Color(0.2f, 0.25f, 0.3f, 0.5f);
                _slotPips[slotIndex] = pip;
                btnSlot.Add(pip);

                VisualElement iconBox = new VisualElement();
                iconBox.pickingMode = PickingMode.Ignore;
                iconBox.style.width = _customLogoSize;
                iconBox.style.height = _customLogoSize;
                iconBox.style.alignSelf = Align.Center;

                Texture2D tex = (slotIndex == 3)
                    ? (_iconTextures.ContainsKey("settings") ? _iconTextures["settings"] : null)
                    : GetTextureForPage(_slotAssignments[slotIndex]);

                if (tex != null)
                {
                    iconBox.style.backgroundImage = new StyleBackground(tex);
                    iconBox.style.unityBackgroundImageTintColor = _logoColor;
                }
                else
                {
                    Label fbLabel = new Label(slotIndex == 3 ? "[*]" : GetPageGlyph(_slotAssignments[slotIndex]));
                    ApplyPixelStyle(fbLabel, 9f, true);
                    fbLabel.style.color = _logoColor;
                    iconBox.Add(fbLabel);
                }

                _slotIconElements[slotIndex] = iconBox;
                btnSlot.Add(iconBox);

                _appBarRoot.Add(btnSlot);
            }

            root.Add(_appBarRoot);
        }

        private void RefreshQuickBarIcons()
        {
            for (int i = 0; i < 4; i++)
            {
                VisualElement box = _slotIconElements[i];
                if (box == null) continue;

                box.Clear();
                box.style.width = _customLogoSize;
                box.style.height = _customLogoSize;

                Texture2D tex = (i == 3)
                    ? (_iconTextures.ContainsKey("settings") ? _iconTextures["settings"] : null)
                    : GetTextureForPage(_slotAssignments[i]);

                if (tex != null)
                {
                    box.style.backgroundImage = new StyleBackground(tex);
                    box.style.unityBackgroundImageTintColor = _logoColor;
                }
                else
                {
                    box.style.backgroundImage = null;
                    Label fbLabel = new Label(i == 3 ? "[*]" : GetPageGlyph(_slotAssignments[i]));
                    ApplyPixelStyle(fbLabel, _customFontSize, true);
                    fbLabel.style.color = _logoColor;
                    box.Add(fbLabel);
                }
            }
        }

        // ================= MAIN TELEMETRY WINDOW =================
        private void BuildMainWindow(VisualElement root)
        {
            _windowRoot = new VisualElement();
            _windowRoot.name = "telemetry-main-window";
            _windowRoot.pickingMode = PickingMode.Position;
            _windowRoot.style.position = UnityEngine.UIElements.Position.Absolute;
            _windowRoot.style.top = 80;
            _windowRoot.style.left = 300;
            _windowRoot.style.width = DefaultWinWidth;
            _windowRoot.style.height = DefaultWinHeight;
            _windowRoot.style.minWidth = MinWidth;
            _windowRoot.style.minHeight = MinHeight;
            _windowRoot.style.borderLeftWidth = 1;
            _windowRoot.style.borderRightWidth = 1;
            _windowRoot.style.borderTopWidth = 1;
            _windowRoot.style.borderBottomWidth = 1;
            _windowRoot.style.borderTopLeftRadius = 4;
            _windowRoot.style.borderTopRightRadius = 4;
            _windowRoot.style.borderBottomLeftRadius = 4;
            _windowRoot.style.borderBottomRightRadius = 4;
            _windowRoot.style.paddingLeft = 7;
            _windowRoot.style.paddingRight = 7;
            _windowRoot.style.paddingTop = 5;
            _windowRoot.style.paddingBottom = 4;

            _winHeaderLine = new VisualElement();
            _winHeaderLine.style.flexDirection = FlexDirection.Row;
            _winHeaderLine.style.alignItems = Align.Center;
            _winHeaderLine.style.justifyContent = Justify.SpaceBetween;
            _winHeaderLine.style.marginBottom = 5;
            _winHeaderLine.style.paddingBottom = 3;
            _winHeaderLine.style.borderBottomWidth = 1;

            _winHeaderLine.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    Vector2 localPos = _windowRoot.parent != null ? _windowRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _winDragOffset = new Vector2(localPos.x - _windowRoot.resolvedStyle.left, localPos.y - _windowRoot.resolvedStyle.top);
                    _isWinDragging = true;
                    _winHeaderLine.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            _winHeaderLine.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isWinDragging && _winHeaderLine.HasPointerCapture(evt.pointerId))
                {
                    Vector2 pPos = _windowRoot.parent != null ? _windowRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _windowRoot.style.left = pPos.x - _winDragOffset.x;
                    _windowRoot.style.top = pPos.y - _winDragOffset.y;
                    evt.StopPropagation();
                }
            });
            _winHeaderLine.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isWinDragging && evt.button == 0)
                {
                    _isWinDragging = false;
                    _winHeaderLine.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            _windowTitle = new Label("// TELEMETRY");
            ApplyPixelStyle(_windowTitle, 10f, true);
            _winHeaderLine.Add(_windowTitle);

            Button closeBtn = new Button(() => CloseTelemetryWindow()) { text = "✕" };
            closeBtn.style.width = 16;
            closeBtn.style.height = 16;
            ApplyPixelStyle(closeBtn, 9f, true);
            closeBtn.style.backgroundColor = new Color(0.4f, 0.15f, 0.15f);
            closeBtn.style.color = Color.white;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            _winHeaderLine.Add(closeBtn);
            _windowRoot.Add(_winHeaderLine);

            _contentScrollView = new ScrollView();
            _contentScrollView.style.flexGrow = 1;
            _windowRoot.Add(_contentScrollView);

            VisualElement footerRow = new VisualElement();
            footerRow.style.flexDirection = FlexDirection.Row;
            footerRow.style.justifyContent = Justify.SpaceBetween;
            footerRow.style.alignItems = Align.Center;
            footerRow.style.marginTop = 2;
            footerRow.style.height = 20;

            VisualElement bottomDragRegion = new VisualElement();
            bottomDragRegion.style.flexGrow = 1;
            bottomDragRegion.style.flexDirection = FlexDirection.Row;
            bottomDragRegion.style.alignItems = Align.Center;
            bottomDragRegion.style.height = 20;

            Label hintLabel = new Label("✥ [DRAG TO MOVE]  •  [F8] TOGGLE");
            ApplyPixelStyle(hintLabel, 8f, false);
            hintLabel.style.color = new Color(0.40f, 0.50f, 0.60f);
            hintLabel.pickingMode = PickingMode.Ignore;
            bottomDragRegion.Add(hintLabel);

            bottomDragRegion.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    Vector2 localPos = _windowRoot.parent != null ? _windowRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _winDragOffset = new Vector2(localPos.x - _windowRoot.resolvedStyle.left, localPos.y - _windowRoot.resolvedStyle.top);
                    _isWinDragging = true;
                    bottomDragRegion.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            bottomDragRegion.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isWinDragging && bottomDragRegion.HasPointerCapture(evt.pointerId))
                {
                    Vector2 pPos = _windowRoot.parent != null ? _windowRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _windowRoot.style.left = pPos.x - _winDragOffset.x;
                    _windowRoot.style.top = pPos.y - _winDragOffset.y;
                    evt.StopPropagation();
                }
            });
            bottomDragRegion.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isWinDragging && evt.button == 0)
                {
                    _isWinDragging = false;
                    bottomDragRegion.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            footerRow.Add(bottomDragRegion);

            VisualElement resizeHandle = new VisualElement();
            resizeHandle.name = "win-resize-handle";
            resizeHandle.style.width = 22;
            resizeHandle.style.height = 22;
            resizeHandle.style.paddingRight = 2;
            resizeHandle.style.paddingBottom = 2;
            resizeHandle.style.alignItems = Align.FlexEnd;
            resizeHandle.style.justifyContent = Justify.FlexEnd;

            VisualElement gripIcon = new VisualElement();
            gripIcon.pickingMode = PickingMode.Ignore;
            gripIcon.style.width = 12;
            gripIcon.style.height = 12;
            gripIcon.style.borderRightWidth = 2;
            gripIcon.style.borderBottomWidth = 2;
            resizeHandle.Add(gripIcon);

            resizeHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _isWinResizing = true;
                    _initialWinPointerPos = evt.position;
                    _initialWinSize = new Vector2(
                        float.IsNaN(_windowRoot.resolvedStyle.width) ? DefaultWinWidth : _windowRoot.resolvedStyle.width,
                        float.IsNaN(_windowRoot.resolvedStyle.height) ? DefaultWinHeight : _windowRoot.resolvedStyle.height
                    );
                    resizeHandle.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            resizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isWinResizing && resizeHandle.HasPointerCapture(evt.pointerId))
                {
                    Vector3 delta = evt.position - _initialWinPointerPos;
                    _windowRoot.style.width = Mathf.Max(MinWidth, _initialWinSize.x + delta.x);
                    _windowRoot.style.height = Mathf.Max(MinHeight, _initialWinSize.y + delta.y);
                    evt.StopPropagation();
                }
            });
            resizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isWinResizing && evt.button == 0)
                {
                    _isWinResizing = false;
                    resizeHandle.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            footerRow.Add(resizeHandle);
            _windowRoot.Add(footerRow);
            root.Add(_windowRoot);
        }

        // ================= INDEPENDENT SETTINGS WINDOW =================
        private void BuildSettingsWindow(VisualElement root)
        {
            _settingsRoot = new VisualElement();
            _settingsRoot.name = "telemetry-settings-window";
            _settingsRoot.pickingMode = PickingMode.Position;
            _settingsRoot.style.position = UnityEngine.UIElements.Position.Absolute;
            _settingsRoot.style.top = 80;
            _settingsRoot.style.left = 640;
            _settingsRoot.style.width = DefaultSettingsWidth;
            _settingsRoot.style.height = DefaultSettingsHeight;
            _settingsRoot.style.minWidth = SettingsMinWidth;
            _settingsRoot.style.minHeight = SettingsMinHeight;
            _settingsRoot.style.borderLeftWidth = 1;
            _settingsRoot.style.borderRightWidth = 1;
            _settingsRoot.style.borderTopWidth = 1;
            _settingsRoot.style.borderBottomWidth = 1;
            _settingsRoot.style.borderTopLeftRadius = 4;
            _settingsRoot.style.borderTopRightRadius = 4;
            _settingsRoot.style.borderBottomLeftRadius = 4;
            _settingsRoot.style.borderBottomRightRadius = 4;
            _settingsRoot.style.paddingLeft = 7;
            _settingsRoot.style.paddingRight = 7;
            _settingsRoot.style.paddingTop = 5;
            _settingsRoot.style.paddingBottom = 4;
            _settingsRoot.style.display = DisplayStyle.None;

            _settingsHeaderLine = new VisualElement();
            _settingsHeaderLine.style.flexDirection = FlexDirection.Row;
            _settingsHeaderLine.style.alignItems = Align.Center;
            _settingsHeaderLine.style.justifyContent = Justify.SpaceBetween;
            _settingsHeaderLine.style.marginBottom = 5;
            _settingsHeaderLine.style.paddingBottom = 3;
            _settingsHeaderLine.style.borderBottomWidth = 1;

            _settingsHeaderLine.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    Vector2 localPos = _settingsRoot.parent != null ? _settingsRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _settingsDragOffset = new Vector2(localPos.x - _settingsRoot.resolvedStyle.left, localPos.y - _settingsRoot.resolvedStyle.top);
                    _isSettingsDragging = true;
                    _settingsHeaderLine.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            _settingsHeaderLine.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isSettingsDragging && _settingsHeaderLine.HasPointerCapture(evt.pointerId))
                {
                    Vector2 pPos = _settingsRoot.parent != null ? _settingsRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _settingsRoot.style.left = pPos.x - _settingsDragOffset.x;
                    _settingsRoot.style.top = pPos.y - _settingsDragOffset.y;
                    evt.StopPropagation();
                }
            });
            _settingsHeaderLine.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isSettingsDragging && evt.button == 0)
                {
                    _isSettingsDragging = false;
                    _settingsHeaderLine.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            VisualElement navGroup = new VisualElement();
            navGroup.style.flexDirection = FlexDirection.Row;
            navGroup.style.alignItems = Align.Center;

            Button prevBtn = new Button(() => SwitchSettingsPage(-1)) { text = "◀" };
            prevBtn.style.width = 18;
            prevBtn.style.height = 18;
            ApplyPixelStyle(prevBtn, 8f, true);
            prevBtn.style.backgroundColor = new Color(0.12f, 0.18f, 0.26f);
            prevBtn.style.color = Color.white;
            navGroup.Add(prevBtn);

            _settingsSubPageTitle = new Label("// PAGE 1: METRICS");
            ApplyPixelStyle(_settingsSubPageTitle, 9.5f, true);
            _settingsSubPageTitle.style.marginLeft = 6;
            _settingsSubPageTitle.style.marginRight = 6;
            navGroup.Add(_settingsSubPageTitle);

            Button nextBtn = new Button(() => SwitchSettingsPage(1)) { text = "▶" };
            nextBtn.style.width = 18;
            nextBtn.style.height = 18;
            ApplyPixelStyle(nextBtn, 8f, true);
            nextBtn.style.backgroundColor = new Color(0.12f, 0.18f, 0.26f);
            nextBtn.style.color = Color.white;
            navGroup.Add(nextBtn);

            _settingsHeaderLine.Add(navGroup);

            Button sCloseBtn = new Button(() => ToggleSettingsWindow(false)) { text = "✕" };
            sCloseBtn.style.width = 16;
            sCloseBtn.style.height = 16;
            ApplyPixelStyle(sCloseBtn, 9f, true);
            sCloseBtn.style.backgroundColor = new Color(0.4f, 0.15f, 0.15f);
            sCloseBtn.style.color = Color.white;
            sCloseBtn.style.borderLeftWidth = 0;
            sCloseBtn.style.borderRightWidth = 0;
            sCloseBtn.style.borderTopWidth = 0;
            sCloseBtn.style.borderBottomWidth = 0;
            _settingsHeaderLine.Add(sCloseBtn);
            _settingsRoot.Add(_settingsHeaderLine);

            _settingsScrollView = new ScrollView();
            _settingsScrollView.style.flexGrow = 1;
            _settingsRoot.Add(_settingsScrollView);

            VisualElement sFooter = new VisualElement();
            sFooter.style.flexDirection = FlexDirection.Row;
            sFooter.style.justifyContent = Justify.SpaceBetween;
            sFooter.style.alignItems = Align.Center;
            sFooter.style.marginTop = 2;
            sFooter.style.height = 20;

            VisualElement sBottomDragRegion = new VisualElement();
            sBottomDragRegion.style.flexGrow = 1;
            sBottomDragRegion.style.flexDirection = FlexDirection.Row;
            sBottomDragRegion.style.alignItems = Align.Center;
            sBottomDragRegion.style.height = 20;

            Label sHintLabel = new Label("✥ [DRAG TO MOVE]");
            ApplyPixelStyle(sHintLabel, 7.5f, false);
            sHintLabel.style.color = new Color(0.40f, 0.50f, 0.60f);
            sHintLabel.pickingMode = PickingMode.Ignore;
            sBottomDragRegion.Add(sHintLabel);

            sBottomDragRegion.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    Vector2 localPos = _settingsRoot.parent != null ? _settingsRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _settingsDragOffset = new Vector2(localPos.x - _settingsRoot.resolvedStyle.left, localPos.y - _settingsRoot.resolvedStyle.top);
                    _isSettingsDragging = true;
                    sBottomDragRegion.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            sBottomDragRegion.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isSettingsDragging && sBottomDragRegion.HasPointerCapture(evt.pointerId))
                {
                    Vector2 pPos = _settingsRoot.parent != null ? _settingsRoot.parent.WorldToLocal(evt.position) : (Vector2)evt.position;
                    _settingsRoot.style.left = pPos.x - _settingsDragOffset.x;
                    _settingsRoot.style.top = pPos.y - _settingsDragOffset.y;
                    evt.StopPropagation();
                }
            });
            sBottomDragRegion.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isSettingsDragging && evt.button == 0)
                {
                    _isSettingsDragging = false;
                    sBottomDragRegion.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            sFooter.Add(sBottomDragRegion);

            VisualElement sResizeHandle = new VisualElement();
            sResizeHandle.name = "settings-resize-handle";
            sResizeHandle.style.width = 22;
            sResizeHandle.style.height = 22;
            sResizeHandle.style.paddingRight = 2;
            sResizeHandle.style.paddingBottom = 2;
            sResizeHandle.style.alignItems = Align.FlexEnd;
            sResizeHandle.style.justifyContent = Justify.FlexEnd;

            VisualElement sGripIcon = new VisualElement();
            sGripIcon.pickingMode = PickingMode.Ignore;
            sGripIcon.style.width = 12;
            sGripIcon.style.height = 12;
            sGripIcon.style.borderRightWidth = 2;
            sGripIcon.style.borderBottomWidth = 2;
            sResizeHandle.Add(sGripIcon);

            sResizeHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _isSettingsResizing = true;
                    _initialSettingsPointerPos = evt.position;
                    _initialSettingsSize = new Vector2(
                        float.IsNaN(_settingsRoot.resolvedStyle.width) ? DefaultSettingsWidth : _settingsRoot.resolvedStyle.width,
                        float.IsNaN(_settingsRoot.resolvedStyle.height) ? DefaultSettingsHeight : _settingsRoot.resolvedStyle.height
                    );
                    sResizeHandle.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            sResizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isSettingsResizing && sResizeHandle.HasPointerCapture(evt.pointerId))
                {
                    Vector3 delta = evt.position - _initialSettingsPointerPos;
                    _settingsRoot.style.width = Mathf.Max(SettingsMinWidth, _initialSettingsSize.x + delta.x);
                    _settingsRoot.style.height = Mathf.Max(SettingsMinHeight, _initialSettingsSize.y + delta.y);
                    evt.StopPropagation();
                }
            });
            sResizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_isSettingsResizing && evt.button == 0)
                {
                    _isSettingsResizing = false;
                    sResizeHandle.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });

            sFooter.Add(sResizeHandle);
            _settingsRoot.Add(sFooter);

            root.Add(_settingsRoot);
        }

        private void SwitchSettingsPage(int direction)
        {
            _settingsSubPage = (_settingsSubPage + direction + 2) % 2;
            _settingsSubPageTitle.text = (_settingsSubPage == 0) ? "// PAGE 1: METRICS" : "// PAGE 2: HUD STYLE";
            RebuildSettingsView();
        }

        // ================= THEME & STYLING =================
        private void ApplyVisualTheme()
        {
            Color cardBorder = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.5f);
            Color bg = new Color(0.027f, 0.051f, 0.078f, 0.95f);

            _appBarRoot.style.opacity = _boxTransparency;
            _windowRoot.style.opacity = _boxTransparency;
            _settingsRoot.style.opacity = _boxTransparency;

            _appBarRoot.style.backgroundColor = bg;
            _appBarRoot.style.borderLeftColor = _accentColor;
            _appBarRoot.style.borderRightColor = _accentColor;
            _appBarRoot.style.borderTopColor = _accentColor;
            _appBarRoot.style.borderBottomColor = _accentColor;

            _windowRoot.style.backgroundColor = bg;
            _windowRoot.style.borderLeftColor = cardBorder;
            _windowRoot.style.borderRightColor = cardBorder;
            _windowRoot.style.borderTopColor = cardBorder;
            _windowRoot.style.borderBottomColor = cardBorder;
            _winHeaderLine.style.borderBottomColor = _accentColor;
            _windowTitle.style.color = _accentColor;

            _settingsRoot.style.backgroundColor = bg;
            _settingsRoot.style.borderLeftColor = _accentColor;
            _settingsRoot.style.borderRightColor = _accentColor;
            _settingsRoot.style.borderTopColor = _accentColor;
            _settingsRoot.style.borderBottomColor = _accentColor;
            _settingsHeaderLine.style.borderBottomColor = _accentColor;
            _settingsSubPageTitle.style.color = _accentColor;

            RefreshQuickBarIcons();

            var winGrip = _windowRoot.Q<VisualElement>("win-resize-handle")?.ElementAt(0);
            if (winGrip != null)
            {
                winGrip.style.borderRightColor = _accentColor;
                winGrip.style.borderBottomColor = _accentColor;
            }
            var setGrip = _settingsRoot.Q<VisualElement>("settings-resize-handle")?.ElementAt(0);
            if (setGrip != null)
            {
                setGrip.style.borderRightColor = _accentColor;
                setGrip.style.borderBottomColor = _accentColor;
            }

            RebuildTelemetryPageView();
            if (_isSettingsVisible) RebuildSettingsView();
        }

        // ================= BUTTON TOGGLES =================
        private void OnSlotClicked(int slotIndex)
        {
            if (slotIndex == 3)
            {
                ToggleSettingsWindow(!_isSettingsVisible);
                return;
            }

            if (_activeSlotIndex == slotIndex && _isWindowVisible)
            {
                CloseTelemetryWindow();
            }
            else
            {
                OpenTelemetrySlot(slotIndex);
            }
        }

        private void OpenTelemetrySlot(int slotIndex)
        {
            _activeSlotIndex = slotIndex;
            _activePage = _slotAssignments[slotIndex];
            _isWindowVisible = true;
            _windowRoot.style.display = DisplayStyle.Flex;

            for (int i = 0; i < 3; i++)
            {
                _slotPips[i].style.backgroundColor = (i == slotIndex)
                    ? new Color(0f, 1f, 0.45f, 1f)
                    : new Color(0.2f, 0.25f, 0.3f, 0.5f);
            }

            RebuildTelemetryPageView();
        }

        private void CloseTelemetryWindow()
        {
            _isWindowVisible = false;
            _activeSlotIndex = -1;
            if (_windowRoot != null) _windowRoot.style.display = DisplayStyle.None;

            for (int i = 0; i < 3; i++)
            {
                if (_slotPips[i] != null) _slotPips[i].style.backgroundColor = new Color(0.2f, 0.25f, 0.3f, 0.5f);
            }
        }

        private void ToggleSettingsWindow(bool show)
        {
            _isSettingsVisible = show;
            _settingsRoot.style.display = _isSettingsVisible ? DisplayStyle.Flex : DisplayStyle.None;

            _slotPips[3].style.backgroundColor = _isSettingsVisible
                ? new Color(0f, 1f, 0.45f, 1f)
                : new Color(0.2f, 0.25f, 0.3f, 0.5f);

            if (_isSettingsVisible)
            {
                RebuildSettingsView();
            }
        }

        // ================= MAIN VIEW RENDERING =================
        private void RebuildTelemetryPageView()
        {
            _contentScrollView.Clear();
            _dynamicLabels.Clear();
            _rowTitleLabels.Clear();
            _rowValueLabels.Clear();

            _windowTitle.text = $"// {GetPageTitle(_activePage)}";

            if (!_pageMetrics.ContainsKey(_activePage))
            {
                _pageMetrics[_activePage] = new List<TelemetryMetric>();
            }

            VisualElement card = CreateCard();
            foreach (var metric in _pageMetrics[_activePage])
            {
                Label val = CreateRow(card, GetMetricLabel(metric), "--");
                _dynamicLabels[metric.ToString()] = val;
            }
            _contentScrollView.Add(card);
        }

        // ================= SETTINGS VIEW RENDERING =================
        private void RebuildSettingsView()
        {
            _settingsScrollView.Clear();

            if (_settingsSubPage == 0)
            {
                BuildSettingsMetricsSubPage();
            }
            else
            {
                BuildSettingsAppearanceSubPage();
            }
        }

        private void BuildSettingsMetricsSubPage()
        {
            Label secHeader = new Label("QUICK-BAR BUTTON MAPPING");
            ApplyPixelStyle(secHeader, 9.5f, true);
            secHeader.style.color = _accentColor;
            secHeader.style.marginBottom = 5;
            _settingsScrollView.Add(secHeader);

            for (int i = 0; i < 3; i++)
            {
                int slotIndex = i;
                VisualElement slotBlock = new VisualElement();
                slotBlock.style.flexDirection = FlexDirection.Column;
                slotBlock.style.marginBottom = 4;

                Label nameLbl = new Label($"Button {slotIndex + 1} Assignment:");
                ApplyPixelStyle(nameLbl, 8.5f, false);
                nameLbl.style.color = new Color(0.7f, 0.8f, 0.9f);
                nameLbl.style.marginBottom = 2;
                slotBlock.Add(nameLbl);

                // Clean name without fake text glyph
                Button cycleBtn = new Button(() => CycleSlotPage(slotIndex))
                {
                    text = GetPageTitle(_slotAssignments[slotIndex])
                };
                ApplyPixelStyle(cycleBtn, 9f, true);
                cycleBtn.style.height = 22;
                cycleBtn.style.backgroundColor = new Color(0.08f, 0.14f, 0.22f);
                cycleBtn.style.color = _accentColor;
                cycleBtn.style.borderLeftColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.4f);
                cycleBtn.style.borderRightColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.4f);
                cycleBtn.style.borderTopColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.4f);
                cycleBtn.style.borderBottomColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.4f);
                slotBlock.Add(cycleBtn);

                _settingsScrollView.Add(slotBlock);
            }

            // Clean Button 4 label without fake glyph
            Label lockLbl = new Label("Button 4: Locked to [ SETTINGS ]");
            ApplyPixelStyle(lockLbl, 8f, false);
            lockLbl.style.color = new Color(0.45f, 0.5f, 0.6f);
            lockLbl.style.marginTop = 1;
            lockLbl.style.marginBottom = 8;
            _settingsScrollView.Add(lockLbl);

            VisualElement editBlock = new VisualElement();
            editBlock.style.flexDirection = FlexDirection.Column;
            editBlock.style.marginBottom = 6;

            Label editHeader = new Label("SELECT PAGE TO CUSTOMIZE:");
            ApplyPixelStyle(editHeader, 9.5f, true);
            editHeader.style.color = _accentColor;
            editHeader.style.marginBottom = 2;
            editBlock.Add(editHeader);

            // Clean selector dropdown button without fake glyph
            Button selectPageDropdownBtn = new Button(CycleEditingPage)
            {
                text = $">> EDITING: {GetPageTitle(_settingsEditingPage)} <<"
            };
            ApplyPixelStyle(selectPageDropdownBtn, 9f, true);
            selectPageDropdownBtn.style.backgroundColor = new Color(0.04f, 0.20f, 0.30f);
            selectPageDropdownBtn.style.color = _accentColor;
            selectPageDropdownBtn.style.borderLeftColor = _accentColor;
            selectPageDropdownBtn.style.borderRightColor = _accentColor;
            selectPageDropdownBtn.style.borderTopColor = _accentColor;
            selectPageDropdownBtn.style.borderBottomColor = _accentColor;
            selectPageDropdownBtn.style.height = 24;
            editBlock.Add(selectPageDropdownBtn);

            Button resetPageBtn = new Button(() =>
            {
                _pageMetrics[_settingsEditingPage] = GetDefaultMetricsForPage(_settingsEditingPage);
                if (_isWindowVisible && _activePage == _settingsEditingPage)
                {
                    RebuildTelemetryPageView();
                }
                RebuildSettingsView();
            })
            {
                text = $"⟲ RESET [{GetPageTitle(_settingsEditingPage)}] TO DEFAULT"
            };
            ApplyPixelStyle(resetPageBtn, 8f, true);
            resetPageBtn.style.height = 20;
            resetPageBtn.style.marginTop = 3;
            resetPageBtn.style.backgroundColor = new Color(0.18f, 0.12f, 0.12f);
            resetPageBtn.style.color = new Color(1f, 0.6f, 0.6f);
            resetPageBtn.style.borderLeftWidth = 0;
            resetPageBtn.style.borderRightWidth = 0;
            resetPageBtn.style.borderTopWidth = 0;
            resetPageBtn.style.borderBottomWidth = 0;
            editBlock.Add(resetPageBtn);

            _settingsScrollView.Add(editBlock);

            VisualElement twoColGrid = new VisualElement();
            twoColGrid.style.flexDirection = FlexDirection.Row;
            twoColGrid.style.flexWrap = Wrap.Wrap;
            twoColGrid.style.justifyContent = Justify.SpaceBetween;

            List<TelemetryMetric> currentList = _pageMetrics[_settingsEditingPage];

            foreach (TelemetryMetric m in Enum.GetValues(typeof(TelemetryMetric)))
            {
                TelemetryMetric metricCopy = m;
                Toggle t = new Toggle(GetMetricLabel(metricCopy))
                {
                    value = currentList.Contains(metricCopy)
                };
                t.style.width = Length.Percent(48);
                ApplyPixelStyle(t, _customFontSize - 0.5f, false);
                t.style.marginBottom = 2;
                t.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue && !currentList.Contains(metricCopy))
                    {
                        currentList.Add(metricCopy);
                    }
                    else if (!evt.newValue)
                    {
                        currentList.Remove(metricCopy);
                    }

                    if (_isWindowVisible && _activePage == _settingsEditingPage)
                    {
                        RebuildTelemetryPageView();
                    }
                });
                twoColGrid.Add(t);
            }

            _settingsScrollView.Add(twoColGrid);
        }

        private void BuildSettingsAppearanceSubPage()
        {
            Button resetAllAppearanceBtn = new Button(ResetAppearanceToDefaults)
            {
                text = "⟲ RESET APPEARANCE TO DEFAULTS"
            };
            ApplyPixelStyle(resetAllAppearanceBtn, 9f, true);
            resetAllAppearanceBtn.style.height = 24;
            resetAllAppearanceBtn.style.marginBottom = 8;
            resetAllAppearanceBtn.style.backgroundColor = new Color(0.28f, 0.14f, 0.14f);
            resetAllAppearanceBtn.style.color = new Color(1f, 0.7f, 0.7f);
            resetAllAppearanceBtn.style.borderLeftWidth = 0;
            resetAllAppearanceBtn.style.borderRightWidth = 0;
            resetAllAppearanceBtn.style.borderTopWidth = 0;
            resetAllAppearanceBtn.style.borderBottomWidth = 0;
            _settingsScrollView.Add(resetAllAppearanceBtn);

            Label colorMixerHeader = new Label("ADVANCED COLOR MIXER (HSV)");
            ApplyPixelStyle(colorMixerHeader, 9.5f, true);
            colorMixerHeader.style.color = _accentColor;
            colorMixerHeader.style.marginBottom = 3;
            _settingsScrollView.Add(colorMixerHeader);

            VisualElement targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.justifyContent = Justify.SpaceBetween;
            targetRow.style.marginBottom = 6;

            foreach (ColorTarget target in Enum.GetValues(typeof(ColorTarget)))
            {
                ColorTarget tCopy = target;
                Button targetBtn = new Button(() =>
                {
                    _selectedColorTarget = tCopy;
                    RebuildSettingsView();
                })
                {
                    text = tCopy.ToString().ToUpper()
                };
                ApplyPixelStyle(targetBtn, 8f, true);
                targetBtn.style.width = Length.Percent(23);
                targetBtn.style.height = 20;

                bool isSelected = (_selectedColorTarget == tCopy);
                targetBtn.style.backgroundColor = isSelected ? new Color(0.04f, 0.20f, 0.30f) : new Color(0.10f, 0.14f, 0.20f);
                targetBtn.style.color = isSelected ? _accentColor : Color.gray;
                targetBtn.style.borderBottomWidth = isSelected ? 2 : 0;
                targetBtn.style.borderBottomColor = _accentColor;
                targetRow.Add(targetBtn);
            }
            _settingsScrollView.Add(targetRow);

            Color activeColor = GetActiveTargetColor();
            Color.RGBToHSV(activeColor, out float currentH, out float currentS, out float currentV);

            VisualElement previewBox = new VisualElement();
            previewBox.style.height = 16;
            previewBox.style.backgroundColor = activeColor;
            previewBox.style.borderLeftWidth = 1;
            previewBox.style.borderRightWidth = 1;
            previewBox.style.borderTopWidth = 1;
            previewBox.style.borderBottomWidth = 1;
            previewBox.style.borderLeftColor = Color.white;
            previewBox.style.borderRightColor = Color.white;
            previewBox.style.borderTopColor = Color.white;
            previewBox.style.borderBottomColor = Color.white;
            previewBox.style.marginBottom = 8;
            _settingsScrollView.Add(previewBox);

            float liveH = currentH;
            float liveS = currentS;
            float liveV = currentV;

            VisualElement hueContainer = new VisualElement();
            hueContainer.style.marginBottom = 4;
            Label hueLabel = new Label($"HUE: {Mathf.RoundToInt(liveH * 360)}°");
            ApplyPixelStyle(hueLabel, 8.5f, false);
            hueLabel.style.color = new Color(0.85f, 0.90f, 0.95f);
            hueContainer.Add(hueLabel);

            Slider hueSlider = new Slider(0f, 1f) { value = liveH, pageSize = 0f };
            hueSlider.style.marginTop = 1;
            hueSlider.RegisterValueChangedCallback(evt =>
            {
                liveH = evt.newValue;
                hueLabel.text = $"HUE: {Mathf.RoundToInt(liveH * 360)}°";
                Color newCol = Color.HSVToRGB(liveH, liveS, liveV);
                previewBox.style.backgroundColor = newCol;
                SetActiveTargetColor(newCol);
                ApplyVisualTheme();
            });
            hueContainer.Add(hueSlider);
            _settingsScrollView.Add(hueContainer);

            VisualElement satContainer = new VisualElement();
            satContainer.style.marginBottom = 4;
            Label satLabel = new Label($"SATURATION: {Mathf.RoundToInt(liveS * 100)}%");
            ApplyPixelStyle(satLabel, 8.5f, false);
            satLabel.style.color = new Color(0.85f, 0.90f, 0.95f);
            satContainer.Add(satLabel);

            Slider satSlider = new Slider(0f, 1f) { value = liveS, pageSize = 0f };
            satSlider.style.marginTop = 1;
            satSlider.RegisterValueChangedCallback(evt =>
            {
                liveS = evt.newValue;
                satLabel.text = $"SATURATION: {Mathf.RoundToInt(liveS * 100)}%";
                Color newCol = Color.HSVToRGB(liveH, liveS, liveV);
                previewBox.style.backgroundColor = newCol;
                SetActiveTargetColor(newCol);
                ApplyVisualTheme();
            });
            satContainer.Add(satSlider);
            _settingsScrollView.Add(satContainer);

            VisualElement valContainer = new VisualElement();
            valContainer.style.marginBottom = 10;
            Label valLabel = new Label($"BRIGHTNESS: {Mathf.RoundToInt(liveV * 100)}%");
            ApplyPixelStyle(valLabel, 8.5f, false);
            valLabel.style.color = new Color(0.85f, 0.90f, 0.95f);
            valContainer.Add(valLabel);

            Slider valSlider = new Slider(0.05f, 1f) { value = liveV, pageSize = 0f };
            valSlider.style.marginTop = 1;
            valSlider.RegisterValueChangedCallback(evt =>
            {
                liveV = evt.newValue;
                valLabel.text = $"BRIGHTNESS: {Mathf.RoundToInt(liveV * 100)}%";
                Color newCol = Color.HSVToRGB(liveH, liveS, liveV);
                previewBox.style.backgroundColor = newCol;
                SetActiveTargetColor(newCol);
                ApplyVisualTheme();
            });
            valContainer.Add(valSlider);
            _settingsScrollView.Add(valContainer);

            Label fontHeader = new Label($"TELEMETRY FONT SIZE: {_customFontSize:F1} pt");
            ApplyPixelStyle(fontHeader, 9.5f, true);
            fontHeader.style.color = _accentColor;
            fontHeader.style.marginBottom = 3;
            _settingsScrollView.Add(fontHeader);

            VisualElement fontRow = new VisualElement();
            fontRow.style.flexDirection = FlexDirection.Row;
            fontRow.style.justifyContent = Justify.SpaceBetween;
            fontRow.style.marginBottom = 8;

            Button decreaseFont = new Button(() =>
            {
                _customFontSize = Mathf.Max(6.5f, _customFontSize - 0.5f);
                RebuildTelemetryPageView();
                RebuildSettingsView();
            })
            { text = "A- (SMALLER)" };
            ApplyPixelStyle(decreaseFont, 8.5f, true);
            decreaseFont.style.width = 155;
            decreaseFont.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            decreaseFont.style.color = Color.white;
            fontRow.Add(decreaseFont);

            Button increaseFont = new Button(() =>
            {
                _customFontSize = Mathf.Min(12.0f, _customFontSize + 0.5f);
                RebuildTelemetryPageView();
                RebuildSettingsView();
            })
            { text = "A+ (LARGER)" };
            ApplyPixelStyle(increaseFont, 8.5f, true);
            increaseFont.style.width = 155;
            increaseFont.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            increaseFont.style.color = Color.white;
            fontRow.Add(increaseFont);

            _settingsScrollView.Add(fontRow);

            Label logoHeader = new Label($"QUICK-BAR LOGO SIZE: {_customLogoSize:F0} px");
            ApplyPixelStyle(logoHeader, 9.5f, true);
            logoHeader.style.color = _accentColor;
            logoHeader.style.marginBottom = 3;
            _settingsScrollView.Add(logoHeader);

            VisualElement logoRow = new VisualElement();
            logoRow.style.flexDirection = FlexDirection.Row;
            logoRow.style.justifyContent = Justify.SpaceBetween;
            logoRow.style.marginBottom = 8;

            Button decLogoBtn = new Button(() =>
            {
                _customLogoSize = Mathf.Max(10f, _customLogoSize - 2f);
                ApplyVisualTheme();
            })
            { text = "LOGO (SMALLER)" };
            ApplyPixelStyle(decLogoBtn, 8.5f, true);
            decLogoBtn.style.width = 155;
            decLogoBtn.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            decLogoBtn.style.color = Color.white;
            logoRow.Add(decLogoBtn);

            Button incLogoBtn = new Button(() =>
            {
                _customLogoSize = Mathf.Min(26f, _customLogoSize + 2f);
                ApplyVisualTheme();
            })
            { text = "LOGO (LARGER)" };
            ApplyPixelStyle(incLogoBtn, 8.5f, true);
            incLogoBtn.style.width = 155;
            incLogoBtn.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            incLogoBtn.style.color = Color.white;
            logoRow.Add(incLogoBtn);

            _settingsScrollView.Add(logoRow);

            Label alphaHeader = new Label($"WINDOW OPACITY: {Mathf.RoundToInt(_boxTransparency * 100)}%");
            ApplyPixelStyle(alphaHeader, 9.5f, true);
            alphaHeader.style.color = _accentColor;
            alphaHeader.style.marginBottom = 3;
            _settingsScrollView.Add(alphaHeader);

            VisualElement alphaRow = new VisualElement();
            alphaRow.style.flexDirection = FlexDirection.Row;
            alphaRow.style.justifyContent = Justify.SpaceBetween;
            alphaRow.style.marginBottom = 8;

            Button decAlpha = new Button(() =>
            {
                _boxTransparency = Mathf.Max(0.20f, _boxTransparency - 0.15f);
                ApplyVisualTheme();
            })
            { text = "- MORE CLEAR" };
            ApplyPixelStyle(decAlpha, 8.5f, true);
            decAlpha.style.width = 155;
            decAlpha.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            decAlpha.style.color = Color.white;
            alphaRow.Add(decAlpha);

            Button incAlpha = new Button(() =>
            {
                _boxTransparency = Mathf.Min(1.00f, _boxTransparency + 0.15f);
                ApplyVisualTheme();
            })
            { text = "+ MORE SOLID" };
            ApplyPixelStyle(incAlpha, 8.5f, true);
            incAlpha.style.width = 155;
            incAlpha.style.backgroundColor = new Color(0.1f, 0.16f, 0.24f);
            incAlpha.style.color = Color.white;
            alphaRow.Add(incAlpha);

            _settingsScrollView.Add(alphaRow);
        }

        private Color GetActiveTargetColor()
        {
            switch (_selectedColorTarget)
            {
                case ColorTarget.Accent: return _accentColor;
                case ColorTarget.Labels: return _labelColor;
                case ColorTarget.Values: return _valueColor;
                case ColorTarget.Logos: return _logoColor;
                default: return _accentColor;
            }
        }

        private void SetActiveTargetColor(Color col)
        {
            switch (_selectedColorTarget)
            {
                case ColorTarget.Accent: _accentColor = col; break;
                case ColorTarget.Labels:
                    _labelColor = col;
                    foreach (var lbl in _rowTitleLabels) lbl.style.color = col;
                    break;
                case ColorTarget.Values:
                    _valueColor = col;
                    foreach (var lbl in _rowValueLabels) lbl.style.color = col;
                    break;
                case ColorTarget.Logos:
                    _logoColor = col;
                    RefreshQuickBarIcons();
                    break;
            }
        }

        private void CycleSlotPage(int slotIndex)
        {
            int current = (int)_slotAssignments[slotIndex];
            int totalPages = Enum.GetValues(typeof(PageType)).Length;
            int next = (current + 1) % totalPages;

            _slotAssignments[slotIndex] = (PageType)next;
            RefreshQuickBarIcons();

            if (_activeSlotIndex == slotIndex)
            {
                _activePage = _slotAssignments[slotIndex];
                RebuildTelemetryPageView();
            }

            RebuildSettingsView();
        }

        private void CycleEditingPage()
        {
            int current = (int)_settingsEditingPage;
            int totalPages = Enum.GetValues(typeof(PageType)).Length;
            _settingsEditingPage = (PageType)((current + 1) % totalPages);

            RebuildSettingsView();
        }

        // ================= GLYPH & TITLE HELPERS =================
        private string GetPageGlyph(PageType page)
        {
            switch (page)
            {
                case PageType.OrbitSubOrbital: return "(O)";
                case PageType.SurfaceDynamics: return "✈";
                case PageType.PropulsionDeltaV: return "Δv";
                case PageType.TargetTracking: return "<+>";
                case PageType.ManeuverNode: return "(*)";
                case PageType.CustomUser1: return "[1]";
                case PageType.CustomUser2: return "[2]";
                default: return "HUD";
            }
        }

        private string GetPageTitle(PageType page)
        {
            switch (page)
            {
                case PageType.OrbitSubOrbital: return "ORBIT / SUB-ORBITAL";
                case PageType.SurfaceDynamics: return "SURFACE DYNAMICS";
                case PageType.PropulsionDeltaV: return "PROPULSION & DELTA-V";
                case PageType.TargetTracking: return "TARGET TRACKING";
                case PageType.ManeuverNode: return "MANEUVER NODE";
                case PageType.CustomUser1: return "USER PAGE 1";
                case PageType.CustomUser2: return "USER PAGE 2";
                default: return "TELEMETRY";
            }
        }

        private string GetMetricLabel(TelemetryMetric metric)
        {
            switch (metric)
            {
                case TelemetryMetric.Apoapsis: return "APOAPSIS (CURRENT)";
                case TelemetryMetric.Periapsis: return "PERIAPSIS (CURRENT)";
                case TelemetryMetric.TimeToAp: return "TIME TO AP (CURRENT)";
                case TelemetryMetric.TimeToPe: return "TIME TO PE (CURRENT)";
                case TelemetryMetric.Inclination: return "INCLINATION";
                case TelemetryMetric.Eccentricity: return "ECCENTRICITY";
                case TelemetryMetric.OrbitPeriod: return "ORBIT PERIOD";
                case TelemetryMetric.ManeuverBurnDv: return "BURN DELTA-V";
                case TelemetryMetric.ManeuverBurnDuration: return "BURN DURATION";
                case TelemetryMetric.ManeuverApoapsis: return "MANEUVER APOAPSIS";
                case TelemetryMetric.ManeuverPeriapsis: return "MANEUVER PERIAPSIS";
                case TelemetryMetric.ManeuverTimeToAp: return "MANEUVER TIME TO AP";
                case TelemetryMetric.ManeuverTimeToPe: return "MANEUVER TIME TO PE";
                case TelemetryMetric.AltTerrain: return "ALT (TERRAIN)";
                case TelemetryMetric.AltSeaLevel: return "ALT (SEA LEVEL)";
                case TelemetryMetric.VertSpeed: return "VERTICAL SPEED";
                case TelemetryMetric.HorizSpeed: return "HORIZONTAL SPEED";
                case TelemetryMetric.SurfaceSpeed: return "SURFACE SPEED";
                case TelemetryMetric.Situation: return "SITUATION";
                case TelemetryMetric.Biome: return "BIOME";
                case TelemetryMetric.TotalDeltaV: return "TOTAL DELTA-V";
                case TelemetryMetric.StageDeltaV: return "STAGE DELTA-V";
                case TelemetryMetric.TargetName: return "TARGET NAME";
                case TelemetryMetric.TargetDistance: return "TARGET DISTANCE";
                case TelemetryMetric.TargetRelSpeed: return "RELATIVE SPEED";
                default: return metric.ToString();
            }
        }

        private VisualElement CreateCard()
        {
            VisualElement card = new VisualElement();
            card.style.backgroundColor = new Color(0.063f, 0.102f, 0.149f, 0.75f);
            card.style.borderLeftWidth = 2;
            card.style.borderLeftColor = _accentColor;
            card.style.borderTopLeftRadius = 2;
            card.style.borderTopRightRadius = 2;
            card.style.borderBottomLeftRadius = 2;
            card.style.borderBottomRightRadius = 2;
            card.style.paddingLeft = 6;
            card.style.paddingRight = 6;
            card.style.paddingTop = 3;
            card.style.paddingBottom = 3;
            card.style.marginBottom = 4;
            return card;
        }

        private Label CreateRow(VisualElement parent, string labelText, string initialVal)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 2;

            Label titleLabel = new Label(labelText);
            ApplyPixelStyle(titleLabel, _customFontSize, false);
            titleLabel.style.color = _labelColor;
            _rowTitleLabels.Add(titleLabel);
            row.Add(titleLabel);

            Label valueLabel = new Label(initialVal);
            ApplyPixelStyle(valueLabel, _customFontSize + 0.5f, true);
            valueLabel.style.color = _valueColor;
            _rowValueLabels.Add(valueLabel);
            row.Add(valueLabel);

            parent.Add(row);
            return valueLabel;
        }

        // ================= UPDATE LOOP & DATA BINDING =================
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (_isWindowVisible)
                    CloseTelemetryWindow();
                else
                    OpenTelemetrySlot(0);
            }

            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            if (!_isWindowVisible) return;
            if (GameManager.Instance == null || GameManager.Instance.Game == null) return;

            VesselComponent vessel = GameManager.Instance.Game.ViewController?.GetActiveSimVessel();
            if (vessel == null) return;

            IKeplerPatch orbit = vessel.Orbit;

            Vector3d srfVel = vessel.SurfaceVelocity.vector;
            Vector3d upUnit = vessel.transform.up.vector;
            double verticalSpeed = Vector3d.Dot(srfVel, upUnit);
            double horizontalSpeed = Vector3d.ProjectOnPlane(srfVel, upUnit).magnitude;

            ManeuverPlanComponent plan = vessel.SimulationObject?.FindComponent<ManeuverPlanComponent>();
            List<ManeuverNodeData> nodes = plan?.GetNodes();
            bool hasNode = (nodes != null && nodes.Count > 0);

            PatchedConicsOrbit postBurnOrbit = null;
            if (hasNode && vessel.Orbiter?.ManeuverPlanSolver != null)
            {
                var conics = vessel.Orbiter.ManeuverPlanSolver.PatchedConicsList;
                if (conics != null && conics.Count > 0)
                {
                    postBurnOrbit = conics[0] as PatchedConicsOrbit;
                }
            }

            foreach (var kvp in _dynamicLabels)
            {
                string key = kvp.Key;
                Label lbl = kvp.Value;
                if (lbl == null) continue;

                if (key == TelemetryMetric.ManeuverBurnDv.ToString())
                    lbl.text = hasNode ? $"{nodes[0].BurnRequiredDV:F1} m/s" : "NO NODE";
                else if (key == TelemetryMetric.ManeuverBurnDuration.ToString())
                    lbl.text = hasNode ? $"{nodes[0].BurnDuration:F1} s" : "--";
                else if (key == TelemetryMetric.ManeuverApoapsis.ToString())
                {
                    if (postBurnOrbit != null)
                        lbl.text = (postBurnOrbit.eccentricity < 1.0) ? $"{postBurnOrbit.ApoapsisArl / 1000.0:N2} km" : "ESCAPE";
                    else
                        lbl.text = "--";
                }
                else if (key == TelemetryMetric.ManeuverPeriapsis.ToString())
                {
                    lbl.text = (postBurnOrbit != null) ? $"{postBurnOrbit.PeriapsisArl / 1000.0:N2} km" : "--";
                }
                else if (key == TelemetryMetric.ManeuverTimeToAp.ToString())
                {
                    if (postBurnOrbit != null && postBurnOrbit.eccentricity < 1.0)
                        lbl.text = $"T - {FormatTimeString(postBurnOrbit.TimeToAp)}";
                    else
                        lbl.text = "--";
                }
                else if (key == TelemetryMetric.ManeuverTimeToPe.ToString())
                {
                    lbl.text = (postBurnOrbit != null) ? $"T - {FormatTimeString(postBurnOrbit.TimeToPe)}" : "--";
                }
                else if (key == TelemetryMetric.Apoapsis.ToString() && orbit != null)
                    lbl.text = (orbit.eccentricity < 1.0) ? $"{orbit.ApoapsisArl / 1000.0:N2} km" : "ESCAPE";
                else if (key == TelemetryMetric.Periapsis.ToString() && orbit != null)
                    lbl.text = $"{orbit.PeriapsisArl / 1000.0:N2} km";
                else if (key == TelemetryMetric.TimeToAp.ToString() && orbit != null)
                    lbl.text = (orbit.eccentricity < 1.0) ? $"T - {FormatTimeString(orbit.TimeToAp)}" : "--";
                else if (key == TelemetryMetric.TimeToPe.ToString() && orbit != null)
                    lbl.text = (orbit.eccentricity < 1.0) ? $"T - {FormatTimeString(orbit.TimeToPe)}" : "--";
                else if (key == TelemetryMetric.Inclination.ToString() && orbit != null)
                    lbl.text = $"{orbit.inclination:F2} deg";
                else if (key == TelemetryMetric.Eccentricity.ToString() && orbit != null)
                    lbl.text = $"{orbit.eccentricity:F4}";
                else if (key == TelemetryMetric.OrbitPeriod.ToString() && orbit != null)
                {
                    bool isSubOrbital = orbit.PeriapsisArl <= 0.0 && orbit.ApoapsisArl > 0.0;
                    lbl.text = isSubOrbital ? "SUB-ORBITAL" : ((orbit.eccentricity < 1.0) ? FormatTimeString(orbit.period) : "ESCAPE");
                }
                else if (key == TelemetryMetric.AltTerrain.ToString())
                    lbl.text = $"{vessel.AltitudeFromTerrain:N1} m";
                else if (key == TelemetryMetric.AltSeaLevel.ToString())
                    lbl.text = $"{vessel.AltitudeFromSeaLevel / 1000.0:N2} km";
                else if (key == TelemetryMetric.VertSpeed.ToString())
                    lbl.text = $"{verticalSpeed:F2} m/s";
                else if (key == TelemetryMetric.HorizSpeed.ToString())
                    lbl.text = $"{horizontalSpeed:F2} m/s";
                else if (key == TelemetryMetric.SurfaceSpeed.ToString())
                    lbl.text = $"{vessel.SurfaceVelocity.magnitude:F1} m/s";
                else if (key == TelemetryMetric.Situation.ToString())
                    lbl.text = vessel.Situation.ToString();
                else if (key == TelemetryMetric.Biome.ToString())
                    lbl.text = vessel.mainBody != null ? vessel.mainBody.bodyName : "--";
                else if (key == TelemetryMetric.TotalDeltaV.ToString())
                    lbl.text = vessel.VesselDeltaV != null ? $"{vessel.VesselDeltaV.TotalDeltaVActual:N0} m/s" : "--";
                else if (key == TelemetryMetric.StageDeltaV.ToString())
                {
                    if (vessel.VesselDeltaV != null && vessel.VesselDeltaV.StageInfo != null && vessel.VesselDeltaV.StageInfo.Count > 0)
                    {
                        var currentStageInfo = vessel.VesselDeltaV.StageInfo[vessel.VesselDeltaV.StageInfo.Count - 1];
                        lbl.text = currentStageInfo != null ? $"{currentStageInfo.DeltaVActual:N0} m/s" : "--";
                    }
                    else lbl.text = "--";
                }
                else if (key == TelemetryMetric.TargetName.ToString())
                    lbl.text = vessel.TargetObject != null ? vessel.TargetObject.DisplayName : "NO TARGET";
                else if (key == TelemetryMetric.TargetDistance.ToString())
                {
                    if (vessel.TargetObject != null && vessel.SimulationObject != null)
                    {
                        double dist = (vessel.SimulationObject.transform.Position - vessel.TargetObject.transform.Position).magnitude;
                        lbl.text = dist > 100000.0 ? $"{dist / 1000000.0:N2} Mm" : $"{dist / 1000.0:N2} km";
                    }
                    else lbl.text = "--";
                }
                else if (key == TelemetryMetric.TargetRelSpeed.ToString())
                {
                    if (vessel.TargetObject != null && vessel.TargetObject.Vessel != null)
                    {
                        double relSpeed = Math.Abs(vessel.OrbitalSpeed - vessel.TargetObject.Vessel.OrbitalSpeed);
                        lbl.text = $"{relSpeed:F1} m/s";
                    }
                    else lbl.text = "--";
                }
            }
        }

        private string FormatTimeString(double totalSeconds)
        {
            if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0) return "--";
            TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
            if (t.TotalDays >= 1) return $"{t.Days}d, {t.Hours}h";
            if (t.TotalHours >= 1) return $"{t.Hours}h, {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m, {t.Seconds:00}s";
            return $"{t.Seconds}s";
        }
    }
}
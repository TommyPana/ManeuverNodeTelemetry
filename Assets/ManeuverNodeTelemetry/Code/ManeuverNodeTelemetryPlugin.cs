using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using Redux.ExtraModTypes;
using SpaceWarp2.API.Mods;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverNodeTelemetry
{
    public class ManeuverNodeTelemetryPlugin : KerbalMod
    {
        private UIDocument _uiDocument;
        private VisualElement _windowRoot;
        private bool _isWindowVisible = true;

        // UI Pages: 0 = Maneuver 1, 1 = Orbit / Sub-Orbital
        private int _currentPage = 0;
        private Label _pageTitleLabel;

        // Shared telemetry labels
        private Label _valApDist;
        private Label _valApTime;
        private Label _valPeDist;
        private Label _valPeTime;
        private Label _headerPe;
        private Label _valInc;
        private Label _valEcc;

        // Maneuver-only telemetry labels
        private VisualElement _maneuverBurnContainer;
        private Label _valBurnDv;
        private Label _valBurnDuration;

        // Orbit / Sub-orbital top card container & labels
        private VisualElement _orbitPeriodContainer;
        private Label _labelPeriodOrLanding;
        private Label _valPeriodOrLanding;

        // Dragging & Resizing state
        private bool _isDragging = false;
        private Vector2 _dragOffset;
        private bool _isResizing = false;
        private Vector3 _initialPointerPos;
        private Vector2 _initialWindowSize;
        private const float MinWidth = 300f;
        private const float MinHeight = 220f;

        public override void OnInitialized()
        {
            base.OnInitialized();
            SWLogger.LogInfo($"{SWMetadata.Name} initialized.");
            MountUI();
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
            BuildTelemetryWindow(_uiDocument.rootVisualElement);
        }

        private void BuildTelemetryWindow(VisualElement root)
        {
            // Main Window Card
            _windowRoot = new VisualElement();
            _windowRoot.name = "telemetry-window";
            _windowRoot.pickingMode = PickingMode.Position;
            _windowRoot.style.position = UnityEngine.UIElements.Position.Absolute;
            _windowRoot.style.top = 140;
            _windowRoot.style.left = 140;
            _windowRoot.style.width = 320;
            _windowRoot.style.minWidth = MinWidth;
            _windowRoot.style.minHeight = MinHeight;
            _windowRoot.style.backgroundColor = new Color(0.027f, 0.051f, 0.078f, 0.94f);
            _windowRoot.style.borderLeftColor = new Color(0f, 0.94f, 1f, 0.5f);
            _windowRoot.style.borderRightColor = new Color(0f, 0.94f, 1f, 0.5f);
            _windowRoot.style.borderTopColor = new Color(0f, 0.94f, 1f, 0.5f);
            _windowRoot.style.borderBottomColor = new Color(0f, 0.94f, 1f, 0.5f);
            _windowRoot.style.borderLeftWidth = 1;
            _windowRoot.style.borderRightWidth = 1;
            _windowRoot.style.borderTopWidth = 1;
            _windowRoot.style.borderBottomWidth = 1;
            _windowRoot.style.borderTopLeftRadius = 4;
            _windowRoot.style.borderTopRightRadius = 4;
            _windowRoot.style.borderBottomLeftRadius = 4;
            _windowRoot.style.borderBottomRightRadius = 4;
            _windowRoot.style.paddingLeft = 8;
            _windowRoot.style.paddingRight = 8;
            _windowRoot.style.paddingTop = 6;
            _windowRoot.style.paddingBottom = 4;

            // --- Header Bar ---
            VisualElement headerBar = new VisualElement();
            headerBar.style.flexDirection = FlexDirection.Row;
            headerBar.style.alignItems = Align.Center;
            headerBar.style.justifyContent = Justify.SpaceBetween;
            headerBar.style.marginBottom = 6;
            headerBar.style.paddingBottom = 4;
            headerBar.style.borderBottomWidth = 1;
            headerBar.style.borderBottomColor = new Color(0f, 0.94f, 1f, 0.25f);

            headerBar.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            headerBar.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            headerBar.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);

            // ✕ Close Button
            Button closeBtn = new Button(() => {
                _isWindowVisible = false;
                _windowRoot.style.display = DisplayStyle.None;
            })
            { text = "✕" };
            StyleHeaderButton(closeBtn, 20, new Color(0.40f, 0.15f, 0.15f));
            headerBar.Add(closeBtn);

            // ◀ Left Page Button
            Button prevBtn = new Button(TogglePage) { text = "◀" };
            StyleHeaderButton(prevBtn, 24, new Color(0.08f, 0.16f, 0.24f));
            headerBar.Add(prevBtn);

            // Title
            _pageTitleLabel = new Label("MANEUVER 1");
            _pageTitleLabel.pickingMode = PickingMode.Ignore;
            _pageTitleLabel.style.fontSize = 12;
            _pageTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pageTitleLabel.style.color = new Color(0f, 0.94f, 1f);
            _pageTitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _pageTitleLabel.style.flexGrow = 1;
            headerBar.Add(_pageTitleLabel);

            // ▶ Right Page Button
            Button nextBtn = new Button(TogglePage) { text = "▶" };
            StyleHeaderButton(nextBtn, 24, new Color(0.08f, 0.16f, 0.24f));
            headerBar.Add(nextBtn);

            _windowRoot.Add(headerBar);

            // --- Section 1: Maneuver Burn Info (Maneuver 1 Page Only) ---
            _maneuverBurnContainer = CreateCard();
            _valBurnDv = CreateRow(_maneuverBurnContainer, "BURN DELTA-V", "0.0 m/s");
            _valBurnDuration = CreateRow(_maneuverBurnContainer, "BURN DURATION", "0.0 s");
            _windowRoot.Add(_maneuverBurnContainer);

            // --- Section 2: Orbit Period / Sub-Orbital Landing (Flight Path Page Only) ---
            _orbitPeriodContainer = CreateCard();
            VisualElement periodRow = new VisualElement();
            periodRow.style.flexDirection = FlexDirection.Row;
            periodRow.style.justifyContent = Justify.SpaceBetween;

            _labelPeriodOrLanding = new Label("ORBIT PERIOD");
            _labelPeriodOrLanding.style.fontSize = 11;
            _labelPeriodOrLanding.style.color = new Color(0.51f, 0.61f, 0.71f);
            periodRow.Add(_labelPeriodOrLanding);

            _valPeriodOrLanding = new Label("--");
            _valPeriodOrLanding.style.fontSize = 11;
            _valPeriodOrLanding.style.unityFontStyleAndWeight = FontStyle.Bold;
            _valPeriodOrLanding.style.color = new Color(0.92f, 0.96f, 1f);
            periodRow.Add(_valPeriodOrLanding);

            _orbitPeriodContainer.Add(periodRow);
            _orbitPeriodContainer.style.display = DisplayStyle.None;
            _windowRoot.Add(_orbitPeriodContainer);

            // --- Section 3: Dual Column Ap / Pe (Both Pages) ---
            VisualElement apsisContainer = new VisualElement();
            apsisContainer.style.flexDirection = FlexDirection.Row;
            apsisContainer.style.justifyContent = Justify.SpaceBetween;
            apsisContainer.style.marginBottom = 6;

            // Apoapsis column
            VisualElement apCol = CreateMetricColumn("APOAPSIS", out _, out _valApDist, out _valApTime);
            apCol.style.flexGrow = 1;
            apCol.style.marginRight = 3;
            apsisContainer.Add(apCol);

            // Periapsis column
            VisualElement peCol = CreateMetricColumn("PERIAPSIS", out _headerPe, out _valPeDist, out _valPeTime);
            peCol.style.flexGrow = 1;
            peCol.style.marginLeft = 3;
            apsisContainer.Add(peCol);

            _windowRoot.Add(apsisContainer);

            // --- Section 4: Inclination & Eccentricity (Both Pages) ---
            VisualElement anglesCard = CreateCard();
            _valInc = CreateRow(anglesCard, "INCLINATION", "0.00 deg");
            _valEcc = CreateRow(anglesCard, "ECCENTRICITY", "0.0000");
            _windowRoot.Add(anglesCard);

            // --- Footer Row & Resizer ---
            VisualElement footerRow = new VisualElement();
            footerRow.style.flexDirection = FlexDirection.Row;
            footerRow.style.justifyContent = Justify.SpaceBetween;
            footerRow.style.alignItems = Align.Center;
            footerRow.style.marginTop = 2;

            Label footerHint = new Label("[F8] Toggle • Drag Header");
            footerHint.style.fontSize = 8;
            footerHint.style.color = new Color(0.35f, 0.43f, 0.51f);
            footerRow.Add(footerHint);

            VisualElement resizeHandle = new VisualElement();
            resizeHandle.style.width = 10;
            resizeHandle.style.height = 10;
            resizeHandle.style.borderRightWidth = 2;
            resizeHandle.style.borderBottomWidth = 2;
            resizeHandle.style.borderRightColor = new Color(0f, 0.94f, 1f, 0.7f);
            resizeHandle.style.borderBottomColor = new Color(0f, 0.94f, 1f, 0.7f);

            resizeHandle.RegisterCallback<PointerDownEvent>(OnResizePointerDown);
            resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizePointerMove);
            resizeHandle.RegisterCallback<PointerUpEvent>(OnResizePointerUp);
            footerRow.Add(resizeHandle);

            _windowRoot.Add(footerRow);
            root.Add(_windowRoot);
        }

        private void TogglePage()
        {
            _currentPage = (_currentPage == 0) ? 1 : 0;

            if (_currentPage == 0)
            {
                _pageTitleLabel.text = "MANEUVER 1";
                _maneuverBurnContainer.style.display = DisplayStyle.Flex;
                _orbitPeriodContainer.style.display = DisplayStyle.None;
            }
            else
            {
                _maneuverBurnContainer.style.display = DisplayStyle.None;
                _orbitPeriodContainer.style.display = DisplayStyle.Flex;
            }
        }

        private VisualElement CreateCard()
        {
            VisualElement card = new VisualElement();
            card.style.backgroundColor = new Color(0.063f, 0.102f, 0.149f, 0.75f);
            card.style.borderLeftWidth = 2;
            card.style.borderLeftColor = new Color(0f, 0.94f, 1f);
            card.style.borderTopLeftRadius = 2;
            card.style.borderTopRightRadius = 2;
            card.style.borderBottomLeftRadius = 2;
            card.style.borderBottomRightRadius = 2;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 4;
            card.style.paddingBottom = 4;
            card.style.marginBottom = 5;
            return card;
        }

        private VisualElement CreateMetricColumn(string title, out Label headerLabel, out Label distLabel, out Label timeLabel)
        {
            VisualElement col = new VisualElement();
            col.style.backgroundColor = new Color(0.063f, 0.102f, 0.149f, 0.75f);
            col.style.borderLeftWidth = 2;
            col.style.borderLeftColor = new Color(0f, 0.94f, 1f);
            col.style.borderTopLeftRadius = 2;
            col.style.borderTopRightRadius = 2;
            col.style.borderBottomLeftRadius = 2;
            col.style.borderBottomRightRadius = 2;
            col.style.paddingLeft = 6;
            col.style.paddingRight = 6;
            col.style.paddingTop = 4;
            col.style.paddingBottom = 4;

            headerLabel = new Label(title);
            headerLabel.style.fontSize = 9;
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.color = new Color(0.51f, 0.61f, 0.71f);
            headerLabel.style.marginBottom = 2;
            col.Add(headerLabel);

            distLabel = new Label("--");
            distLabel.style.fontSize = 11;
            distLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            distLabel.style.color = new Color(0.92f, 0.96f, 1f);
            col.Add(distLabel);

            timeLabel = new Label("--");
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(0f, 0.94f, 1f, 0.85f);
            timeLabel.style.marginTop = 1;
            col.Add(timeLabel);

            return col;
        }

        private Label CreateRow(VisualElement parent, string labelText, string initialVal)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = 2;

            Label titleLabel = new Label(labelText);
            titleLabel.style.fontSize = 11;
            titleLabel.style.color = new Color(0.51f, 0.61f, 0.71f);
            row.Add(titleLabel);

            Label valueLabel = new Label(initialVal);
            valueLabel.style.fontSize = 11;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.style.color = new Color(0.92f, 0.96f, 1f);
            row.Add(valueLabel);

            parent.Add(row);
            return valueLabel;
        }

        private void StyleHeaderButton(Button btn, float width, Color bg)
        {
            btn.style.width = width;
            btn.style.height = 18;
            btn.style.backgroundColor = bg;
            btn.style.borderLeftColor = new Color(0f, 0.94f, 1f, 0.3f);
            btn.style.borderRightColor = new Color(0f, 0.94f, 1f, 0.3f);
            btn.style.borderTopColor = new Color(0f, 0.94f, 1f, 0.3f);
            btn.style.borderBottomColor = new Color(0f, 0.94f, 1f, 0.3f);
            btn.style.borderLeftWidth = 1;
            btn.style.borderRightWidth = 1;
            btn.style.borderTopWidth = 1;
            btn.style.borderBottomWidth = 1;
            btn.style.borderTopLeftRadius = 2;
            btn.style.borderTopRightRadius = 2;
            btn.style.borderBottomLeftRadius = 2;
            btn.style.borderBottomRightRadius = 2;
            btn.style.color = Color.white;
            btn.style.fontSize = 10;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
        }

        // --- Dragging Handlers ---
        private void OnHeaderPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0 && _windowRoot != null)
            {
                _isDragging = true;
                _dragOffset = evt.localPosition;
                ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        private void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (_isDragging && _windowRoot != null && ((VisualElement)evt.currentTarget).HasPointerCapture(evt.pointerId))
            {
                Vector2 parentPos = _windowRoot.parent != null
                    ? _windowRoot.parent.WorldToLocal(evt.position)
                    : (Vector2)evt.position;

                _windowRoot.style.left = parentPos.x - _dragOffset.x;
                _windowRoot.style.top = parentPos.y - _dragOffset.y;
                evt.StopPropagation();
            }
        }

        private void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (_isDragging && evt.button == 0)
            {
                _isDragging = false;
                VisualElement target = (VisualElement)evt.currentTarget;
                if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        // --- Resizing Handlers ---
        private void OnResizePointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0 && _windowRoot != null)
            {
                _isResizing = true;
                _initialPointerPos = evt.position;
                _initialWindowSize = new Vector2(
                    float.IsNaN(_windowRoot.resolvedStyle.width) ? 320 : _windowRoot.resolvedStyle.width,
                    float.IsNaN(_windowRoot.resolvedStyle.height) ? 240 : _windowRoot.resolvedStyle.height
                );
                ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        private void OnResizePointerMove(PointerMoveEvent evt)
        {
            if (_isResizing && _windowRoot != null && ((VisualElement)evt.currentTarget).HasPointerCapture(evt.pointerId))
            {
                Vector3 delta = evt.position - _initialPointerPos;
                _windowRoot.style.width = Mathf.Max(MinWidth, _initialWindowSize.x + delta.x);
                _windowRoot.style.height = Mathf.Max(MinHeight, _initialWindowSize.y + delta.y);
                evt.StopPropagation();
            }
        }

        private void OnResizePointerUp(PointerUpEvent evt)
        {
            if (_isResizing && evt.button == 0)
            {
                _isResizing = false;
                VisualElement target = (VisualElement)evt.currentTarget;
                if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _isWindowVisible = !_isWindowVisible;
                if (_windowRoot != null)
                {
                    _windowRoot.style.display = _isWindowVisible ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            if (_windowRoot == null || !_isWindowVisible) return;
            if (GameManager.Instance == null || GameManager.Instance.Game == null) return;

            VesselComponent activeVessel = GameManager.Instance.Game.ViewController?.GetActiveSimVessel();
            if (activeVessel == null) return;

            if (_currentPage == 0)
            {
                // ================= PAGE 0: MANEUVER 1 =================
                _pageTitleLabel.text = "MANEUVER 1";
                _headerPe.text = "PERIAPSIS";

                ManeuverPlanComponent plan = activeVessel.SimulationObject?.FindComponent<ManeuverPlanComponent>();
                List<ManeuverNodeData> nodes = plan?.GetNodes();

                if (nodes == null || nodes.Count == 0)
                {
                    _valBurnDv.text = "NO NODE";
                    _valBurnDuration.text = "--";
                    _valApDist.text = "--";
                    _valApTime.text = "--";
                    _valPeDist.text = "--";
                    _valPeTime.text = "--";
                    _valInc.text = "--";
                    _valEcc.text = "--";
                    return;
                }

                ManeuverNodeData node = nodes[0];
                _valBurnDv.text = $"{node.BurnRequiredDV:F1} m/s";
                _valBurnDuration.text = $"{node.BurnDuration:F1} s";

                ManeuverPlanSolver solver = activeVessel.Orbiter?.ManeuverPlanSolver;
                var conics = solver?.PatchedConicsList;
                PatchedConicsOrbit postBurnOrbit = (conics != null && conics.Count > 0)
                    ? conics[0] as PatchedConicsOrbit
                    : null;

                if (postBurnOrbit != null)
                {
                    bool isClosed = postBurnOrbit.eccentricity < 1.0;
                    _valApDist.text = isClosed ? $"{postBurnOrbit.ApoapsisArl / 1000.0:N2} km" : "ESCAPE";
                    _valApTime.text = isClosed ? $"T - {FormatTimeString(postBurnOrbit.TimeToAp)}" : "--";

                    _valPeDist.text = $"{postBurnOrbit.PeriapsisArl / 1000.0:N2} km";
                    _valPeTime.text = $"T - {FormatTimeString(postBurnOrbit.TimeToPe)}";

                    _valInc.text = $"{postBurnOrbit.inclination:F2} deg";
                    _valEcc.text = $"{postBurnOrbit.eccentricity:F4}";
                }
            }
            else
            {
                // ================= PAGE 1: ORBIT / SUB-ORBITAL =================
                IKeplerPatch orbit = activeVessel.Orbit;
                if (orbit == null) return;

                bool isSubOrbital = orbit.PeriapsisArl <= 0.0 && orbit.ApoapsisArl > 0.0;
                bool isEscape = orbit.eccentricity >= 1.0;

                if (isSubOrbital)
                {
                    _pageTitleLabel.text = "SUB-ORBITAL";
                    _labelPeriodOrLanding.text = "EST. IMPACT / LANDING";
                    _headerPe.text = "IMPACT PERIAPSIS";

                    double timeToGround = CalculateTimeToAltitude(orbit, 0.0);
                    _valPeriodOrLanding.text = timeToGround >= 0 ? $"T - {FormatTimeString(timeToGround)}" : "--";
                }
                else
                {
                    _pageTitleLabel.text = "ORBIT";
                    _labelPeriodOrLanding.text = "ORBIT PERIOD";
                    _headerPe.text = "PERIAPSIS";

                    _valPeriodOrLanding.text = isEscape ? "ESCAPE" : FormatTimeString(orbit.period);
                }

                // Apoapsis
                _valApDist.text = isEscape ? "ESCAPE" : $"{orbit.ApoapsisArl / 1000.0:N2} km";
                _valApTime.text = isEscape ? "--" : $"T - {FormatTimeString(orbit.TimeToAp)}";

                // Periapsis
                _valPeDist.text = $"{orbit.PeriapsisArl / 1000.0:N2} km";
                _valPeTime.text = isEscape ? "--" : $"T - {FormatTimeString(orbit.TimeToPe)}";

                // Inclination & Eccentricity
                _valInc.text = $"{orbit.inclination:F2} deg";
                _valEcc.text = $"{orbit.eccentricity:F4}";
            }
        }

        // Computes time in seconds until the trajectory reaches targetAltitude relative to sea level
        private double CalculateTimeToAltitude(IKeplerPatch orbit, double targetAltitude)
        {
            if (orbit == null || orbit.referenceBody == null) return -1.0;

            double targetRadius = orbit.referenceBody.radius + targetAltitude;
            double a = orbit.semiMajorAxis;
            double e = orbit.eccentricity;

            if (e < 0.0001 || targetRadius < (a * (1.0 - e)) || targetRadius > (a * (1.0 + e)))
            {
                return orbit.TimeToPe;
            }

            double p = a * (1.0 - (e * e));
            double cosNu = (p / targetRadius - 1.0) / e;
            cosNu = Math.Max(-1.0, Math.Min(1.0, cosNu));
            double nu = Math.Acos(cosNu);

            double impactNu = (2.0 * Math.PI) - nu;

            double cosE = (e + Math.Cos(impactNu)) / (1.0 + (e * Math.Cos(impactNu)));
            cosE = Math.Max(-1.0, Math.Min(1.0, cosE));
            double E = Math.Acos(cosE);
            if (impactNu > Math.PI) E = (2.0 * Math.PI) - E;

            double M = E - (e * Math.Sin(E));
            double meanMotion = Math.Sqrt(orbit.referenceBody.gravParameter / Math.Pow(Math.Abs(a), 3));
            if (meanMotion <= 0.000001) return orbit.TimeToPe;

            double timeFromPe = M / meanMotion;
            double timeToPe = orbit.TimeToPe;

            double timeToImpact = timeToPe - (orbit.period - timeFromPe);
            while (timeToImpact < 0) timeToImpact += orbit.period;

            return timeToImpact;
        }

        private string FormatTimeString(double totalSeconds)
        {
            if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0) return "--";

            TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
            if (t.TotalDays >= 1)
                return $"{t.Days}d, {t.Hours}h";
            if (t.TotalHours >= 1)
                return $"{t.Hours}h, {t.Minutes}m";
            if (t.TotalMinutes >= 1)
                return $"{t.Minutes}m, {t.Seconds:00}s";
            return $"{t.Seconds}s";
        }
    }
}
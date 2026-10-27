using Godot;
using DynamiteUniverse.Game;
using GodotEnvironment = Godot.Environment;

namespace DynamiteUniverse.Ui;

/// <summary>Blender-authored home reactor rendered in a transparent 3D viewport.</summary>
public partial class HomeEnergyReactor : Control
{
    private const string ModelPath = "res://assets/models/detonation-orrery-mk2.glb";
    private const float CameraFovDegrees = 30f;
    private const float CameraElevationDegrees = 10f;
    private const float CameraMargin = 1.16f;
    private const double MotionLoopSeconds = 8.0;

    private SubViewport? _viewport;
    private Camera3D? _camera;
    private Node3D? _modelRoot;
    private AnimationPlayer? _motionPlayer;
    private double _pulseTime;
    private readonly List<PulsedLight> _pulseLights = new();
    private readonly List<PulsedMaterial> _emissiveMaterials = new();
    private static readonly StringName MotionClipName = "home_loop";

    private readonly record struct PulsedLight(OmniLight3D Light, float BaseEnergy, float Phase);
    private readonly record struct PulsedMaterial(BaseMaterial3D Material, float BaseEnergy, float Phase);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);

        var container = new SubViewportContainer
        {
            Stretch = true,
            StretchShrink = 1,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        AddChild(container);

        _viewport = new SubViewport
        {
            TransparentBg = true,
            GuiDisableInput = true,
            UseHdr2D = true,
            // The reactor is a small, high-contrast 3D element. Keep AA local to this viewport.
            Msaa3D = Viewport.Msaa.Msaa4X,
            Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear,
            Scaling3DScale = 1.5f,
            Size = new Vector2I(900, 830),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(_viewport);

        var root = new Node3D { Name = "HomeEnergyReactorRoot" };
        _viewport.AddChild(root);

        // The sky is used only as an HDR reflection probe; the viewport stays transparent.
        var reflectionSky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.035f, 0.105f, 0.24f),
                // Desaturate the reflection horizon so violet energy parts do not disappear into it.
                SkyHorizonColor = new Color(0.15f, 0.09f, 0.24f),
                GroundBottomColor = new Color(0.008f, 0.015f, 0.05f),
                GroundHorizonColor = new Color(0.045f, 0.11f, 0.23f),
                SkyEnergyMultiplier = 0.85f,
                GroundEnergyMultiplier = 0.42f,
                EnergyMultiplier = 0.80f,
            },
        };

        var environment = new GodotEnvironment
        {
            BackgroundMode = GodotEnvironment.BGMode.Color,
            BackgroundColor = new Color(0f, 0f, 0f, 0f),
            Sky = reflectionSky,
            ReflectedLightSource = GodotEnvironment.ReflectionSource.Sky,
            AmbientLightSource = GodotEnvironment.AmbientSource.Color,
            // Lift the dark metal under the mobile renderer; the sky is only a
            // reflection source and cannot provide Blender-like fill by itself.
            AmbientLightColor = new Color(0.42f, 0.58f, 0.86f),
            AmbientLightEnergy = 0.72f,
            TonemapMode = GodotEnvironment.ToneMapper.Filmic,
            TonemapExposure = 1.02f,
            TonemapWhite = 5.5f,
            GlowEnabled = true,
            GlowNormalized = true,
            GlowIntensity = 0.56f,
            GlowStrength = 0.54f,
            GlowBloom = 0.04f,
            GlowBlendMode = GodotEnvironment.GlowBlendModeEnum.Screen,
            GlowHdrThreshold = 1.0f,
            GlowHdrScale = 1.25f,
        };
        root.AddChild(new WorldEnvironment
        {
            Name = "ReactorEnvironment",
            Environment = environment,
        });

        _camera = new Camera3D
        {
            Name = "ReactorCamera",
            Fov = CameraFovDegrees,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Current = true,
        };
        root.AddChild(_camera);

        var key = new DirectionalLight3D
        {
            Name = "CyanKey",
            RotationDegrees = new Vector3(-42f, -30f, -15f),
            LightColor = new Color(0.22f, 0.78f, 1f),
            LightEnergy = 2.8f,
        };
        root.AddChild(key);

        var rim = new DirectionalLight3D
        {
            Name = "PinkRim",
            RotationDegrees = new Vector3(25f, 150f, 12f),
            LightColor = new Color(1f, 0.24f, 0.52f),
            LightEnergy = 1.85f,
        };
        root.AddChild(rim);

        var fill = new OmniLight3D
        {
            Name = "CoreFill",
            Position = new Vector3(0f, 0.35f, 1.6f),
            LightColor = new Color(0.40f, 0.70f, 1f),
            LightEnergy = 2.45f,
            OmniRange = 12f,
            ShadowEnabled = false,
        };
        root.AddChild(fill);
        _pulseLights.Add(new PulsedLight(fill, fill.LightEnergy, 0f));

        // A broad, shadow-free front fill keeps structural surfaces readable.
        // It is a single mobile-friendly light and does not affect the glow pulse.
        var readabilityFill = new OmniLight3D
        {
            Name = "ReadabilityFill",
            Position = new Vector3(0f, 2.5f, 10f),
            // Slightly neutral cool-white fill keeps the alloy silver-blue instead of navy.
            LightColor = new Color(0.80f, 0.89f, 1f),
            LightEnergy = 3.35f,
            OmniRange = 22f,
            ShadowEnabled = false,
        };
        root.AddChild(readabilityFill);

        var back = new DirectionalLight3D
        {
            Name = "BackSeparation",
            RotationDegrees = new Vector3(10f, 180f, 0f),
            LightColor = new Color(0.5f, 0.7f, 1f),
            LightEnergy = 1.0f,
        };
        root.AddChild(back);

        var modelScene = GD.Load<PackedScene>(ModelPath);
        if (modelScene is null)
        {
            GD.PrintErr($"HomeEnergyReactor: failed to load {ModelPath}");
            return;
        }

        _modelRoot = new Node3D { Name = "ModelWrapper" };
        root.AddChild(_modelRoot);

        var model = modelScene.Instantiate<Node3D>();
        model.Name = "DetonationOrrery";
        _modelRoot.AddChild(model);
        ConfigureHolographicMaterials(model);
        AddOrbitPulseLights(model);

        var sourcePlayer = FindAnimationPlayer(model);
        if (sourcePlayer is null)
        {
            GD.PrintErr("HomeEnergyReactor: GLB did not expose an AnimationPlayer");
            return;
        }

        var names = sourcePlayer.GetAnimationList();
        if (names.Count() == 0)
        {
            GD.PrintErr("HomeEnergyReactor: GLB AnimationPlayer has no clips");
            return;
        }

        ConfigureMotionPlayers(model, sourcePlayer, names);

        CallDeferred(nameof(DeferredFitCamera), model);
        ApplyMotionMode();
    }

    public override void _Process(double delta)
    {
        ApplyMotionMode();
        UpdateEnergyPulse(delta);
    }

    private void UpdateEnergyPulse(double delta)
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            SetPulseMultiplier(1f);
            return;
        }

        // Keep the reduced mode calm, but make the full-mode pulse clearly visible.
        _pulseTime += delta * (profile.AllowLoop ? 1.0 : 0.45);
        foreach (var light in _pulseLights)
            light.Light.LightEnergy = light.BaseEnergy * PulseMultiplier(light.Phase);
        foreach (var material in _emissiveMaterials)
            material.Material.EmissionEnergyMultiplier = material.BaseEnergy * PulseMultiplier(material.Phase);
    }

    private void SetPulseMultiplier(float multiplier)
    {
        foreach (var light in _pulseLights)
            light.Light.LightEnergy = light.BaseEnergy * multiplier;
        foreach (var material in _emissiveMaterials)
            material.Material.EmissionEnergyMultiplier = material.BaseEnergy * multiplier;
    }

    private float PulseMultiplier(float phase)
    {
        var time = (float)_pulseTime;
        var breath = 0.5f + 0.5f * Mathf.Sin(time * 5.40f + phase);
        var shimmer = 0.5f + 0.5f * Mathf.Sin(time * 11.20f + phase * 1.7f);

        // A wide 0.45x-1.48x range makes the light read as a pulse on
        // mobile displays; the small high-frequency term adds an electronic flicker.
        return 0.45f + breath * 0.85f + shimmer * 0.18f;
    }

    private void DeferredFitCamera(Node3D model)
    {
        FitCameraToModel(model);
    }

    private void FitCameraToModel(Node3D model)
    {
        if (_camera is null || _modelRoot is null)
            return;

        var aabb = ComputeAabb(model);
        if (aabb.Size.LengthSquared() < 0.001f)
        {
            GD.PrintErr("HomeEnergyReactor: model AABB is empty, using default camera");
            _camera.Position = new Vector3(0f, 2f, 20f);
            _camera.LookAtFromPosition(_camera.Position, Vector3.Zero, Vector3.Up);
            return;
        }

        var center = aabb.GetCenter();
        var size = aabb.Size;
        var elevationRadians = Mathf.DegToRad(CameraElevationDegrees);
        var halfVertical = 0.5f * (size.Y * Mathf.Cos(elevationRadians)
            + size.Z * Mathf.Sin(elevationRadians));
        var halfHorizontal = size.X * 0.5f;
        var aspect = _viewport is not null && _viewport.Size.Y > 0
            ? (float)_viewport.Size.X / _viewport.Size.Y
            : 1.08f;
        var verticalHalfFov = Mathf.DegToRad(CameraFovDegrees * 0.5f);
        var horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * aspect);
        var distance = Mathf.Max(
            halfVertical / Mathf.Tan(verticalHalfFov),
            halfHorizontal / Mathf.Tan(horizontalHalfFov)) * CameraMargin;
        var elevation = Mathf.Sin(elevationRadians) * distance;
        var forward = Mathf.Cos(elevationRadians) * distance;
        var cameraPos = center + new Vector3(0f, elevation, forward);

        _camera.Position = cameraPos;
        _camera.LookAtFromPosition(cameraPos, center, Vector3.Up);
        _camera.Near = Mathf.Max(0.02f, distance * 0.01f);
        _camera.Far = distance + Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 2.5f;

        GD.Print($"HomeEnergyReactor: AABB center={center}, size={size}, fitDistance={distance}, camera={cameraPos}");
    }

    private static Aabb ComputeAabb(Node3D node)
    {
        var aabb = new Aabb();
        var first = true;

        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var meshAabb = mesh.GetAabb();
                var globalAabb = mesh.GlobalTransform * meshAabb;
                if (first)
                {
                    aabb = globalAabb;
                    first = false;
                }
                else
                {
                    aabb = aabb.Merge(globalAabb);
                }
            }
            else if (child is Node3D child3d)
            {
                var childAabb = ComputeAabb(child3d);
                if (childAabb.Size.LengthSquared() > 0.001f)
                {
                    if (first)
                    {
                        aabb = childAabb;
                        first = false;
                    }
                    else
                    {
                        aabb = aabb.Merge(childAabb);
                    }
                }
            }
        }

        return aabb;
    }

    private void ApplyMotionMode()
    {
        if (_motionPlayer is null)
            return;

        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            _motionPlayer.Stop();
            _motionPlayer.Seek(2.8f, true);
            return;
        }

        var speed = profile.AllowLoop ? 1f : 0.18f;
        if (!_motionPlayer.IsPlaying())
            _motionPlayer.Play(MotionClipName, -1.0, speed, false);
        else
            _motionPlayer.SpeedScale = speed;
    }

    private void ConfigureMotionPlayers(Node3D model, AnimationPlayer sourcePlayer, string[] names)
    {
        sourcePlayer.Stop();
        sourcePlayer.Active = false;

        var composite = new Animation
        {
            Length = MotionLoopSeconds,
            LoopMode = Animation.LoopModeEnum.Linear,
        };
        foreach (var name in names)
        {
            var source = sourcePlayer.GetAnimation(name);
            if (source is null)
                continue;

            var timeScale = source.Length > 0.0 ? MotionLoopSeconds / source.Length : 1.0;
            CopyAnimatedTracks(source, composite, timeScale);
        }

        if (composite.GetTrackCount() == 0)
        {
            GD.PrintErr("HomeEnergyReactor: GLB animations contain no changing tracks");
            return;
        }

        var library = new AnimationLibrary();
        var error = library.AddAnimation(MotionClipName, composite);
        if (error != Error.Ok)
        {
            GD.PrintErr($"HomeEnergyReactor: failed to bind composite animation ({error})");
            return;
        }

        _motionPlayer = new AnimationPlayer { Name = "HomeMotion" };
        _motionPlayer.AddAnimationLibrary("", library);
        model.AddChild(_motionPlayer);
        _motionPlayer.RootNode = _motionPlayer.GetPathTo(model);

        GD.Print($"HomeEnergyReactor: composed {composite.GetTrackCount()} moving tracks from {names.Length} clips");
    }

    private static void CopyAnimatedTracks(
        Animation source, Animation destination, double timeScale)
    {
        for (var sourceTrack = 0; sourceTrack < source.GetTrackCount(); sourceTrack++)
        {
            var keyCount = source.TrackGetKeyCount(sourceTrack);
            if (keyCount <= 1)
                continue;

            var trackType = source.TrackGetType(sourceTrack);
            var destinationTrack = destination.AddTrack(trackType);
            destination.TrackSetPath(destinationTrack, source.TrackGetPath(sourceTrack));
            destination.TrackSetInterpolationType(
                destinationTrack, source.TrackGetInterpolationType(sourceTrack));
            destination.TrackSetInterpolationLoopWrap(destinationTrack, true);
            if (trackType == Animation.TrackType.Value)
                destination.ValueTrackSetUpdateMode(
                    destinationTrack, source.ValueTrackGetUpdateMode(sourceTrack));

            for (var key = 0; key < keyCount; key++)
            {
                destination.TrackInsertKey(
                    destinationTrack,
                    source.TrackGetKeyTime(sourceTrack, key) * timeScale,
                    source.TrackGetKeyValue(sourceTrack, key),
                    source.TrackGetKeyTransition(sourceTrack, key));
            }
        }
    }

    private void AddOrbitPulseLights(Node3D model)
    {
        AddOrbitPulseLight(model, "Cyan axial orbit energy pod 1", "CyanOrbitLight", new Color(0.08f, 0.82f, 1f), 2.60f, 0f);
        AddOrbitPulseLight(model, "Pink polar orbit energy pod 1", "PinkOrbitLight", new Color(1f, 0.20f, 0.68f), 2.45f, 2.10f);
        AddOrbitPulseLight(model, "Violet horizon orbit energy pod 1", "VioletOrbitLight", new Color(0.82f, 0.40f, 1f), 2.45f, 4.20f);
    }

    private void AddOrbitPulseLight(
        Node3D model, string anchorName, string lightName, Color color, float energy, float phase)
    {
        var anchor = FindNode3DByName(model, anchorName);
        if (anchor is null)
        {
            GD.PrintErr($"HomeEnergyReactor: missing orbit light anchor {anchorName}");
            return;
        }

        var light = new OmniLight3D
        {
            Name = lightName,
            LightColor = color,
            LightEnergy = energy,
            OmniRange = 3.6f,
            ShadowEnabled = false,
        };
        anchor.AddChild(light);
        _pulseLights.Add(new PulsedLight(light, energy, phase));
    }

    private void ConfigureHolographicMaterials(Node3D node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    var source = mesh.GetSurfaceOverrideMaterial(surface)
                        ?? mesh.Mesh.SurfaceGetMaterial(surface);
                    if (source is not BaseMaterial3D baseMaterial)
                        continue;

                    var tuned = baseMaterial.Duplicate() as BaseMaterial3D;
                    if (tuned is null)
                        continue;

                    var materialName = source.ResourceName;
                    var hasEnergyEmission = tuned.EmissionEnabled;

                    // Godot's built-in rim pass gives a mobile-friendly holographic edge.
                    tuned.RimEnabled = true;
                    tuned.Rim = Mathf.Max(tuned.Rim, 0.34f);
                    tuned.RimTint = 0.24f;

                    if (materialName.Contains("Orbit Violet", StringComparison.OrdinalIgnoreCase))
                    {
                        // Pull the violet energy surfaces away from the dark backdrop.
                        tuned.AlbedoColor = new Color(0.42f, 0.16f, 0.96f, tuned.AlbedoColor.A);
                        tuned.EmissionEnabled = true;
                        tuned.Emission = new Color(0.30f, 0.08f, 1f);
                        tuned.EmissionEnergyMultiplier = Mathf.Max(tuned.EmissionEnergyMultiplier, 0.90f);
                        tuned.Rim = Mathf.Max(tuned.Rim, 0.46f);
                    }
                    else if (materialName.Contains("Orbit Pink", StringComparison.OrdinalIgnoreCase))
                    {
                        tuned.AlbedoColor = new Color(1f, 0.22f, 0.62f, tuned.AlbedoColor.A);
                        tuned.EmissionEnabled = true;
                        tuned.Emission = new Color(1f, 0.10f, 0.42f);
                        tuned.EmissionEnergyMultiplier = Mathf.Max(tuned.EmissionEnergyMultiplier, 0.85f);
                        tuned.Rim = Mathf.Max(tuned.Rim, 0.42f);
                    }

                    if (IsStructuralMaterial(materialName))
                    {
                        // High metallic values become nearly black when the
                        // transparent viewport has no authored reflection probe.
                        tuned.Metallic = Mathf.Min(tuned.Metallic, 0.50f);
                        tuned.Roughness = Mathf.Max(tuned.Roughness, 0.32f);

                        var albedo = tuned.AlbedoColor;
                        tuned.AlbedoColor = new Color(
                            Mathf.Clamp(Mathf.Max(albedo.R * 1.35f, 0.18f), 0f, 1f),
                            Mathf.Clamp(Mathf.Max(albedo.G * 1.35f, 0.28f), 0f, 1f),
                            Mathf.Clamp(Mathf.Max(albedo.B * 1.35f, 0.46f), 0f, 1f),
                            albedo.A);
                        tuned.EmissionEnabled = true;
                        tuned.Emission = new Color(0.05f, 0.14f, 0.32f);
                        tuned.EmissionEnergyMultiplier = 0.32f;
                    }

                    mesh.SetSurfaceOverrideMaterial(surface, tuned);
                    if (hasEnergyEmission || materialName.Contains("Orbit ", StringComparison.OrdinalIgnoreCase))
                        _emissiveMaterials.Add(new PulsedMaterial(
                            tuned, tuned.EmissionEnergyMultiplier, GetEmissionPhase(source.ResourceName)));
                }
            }

            if (child is Node3D child3d)
                ConfigureHolographicMaterials(child3d);
        }
    }

    private static bool IsStructuralMaterial(string materialName)
    {
        return materialName.Contains("Obsidian Alloy", StringComparison.OrdinalIgnoreCase)
            || materialName.Contains("Machined Edge", StringComparison.OrdinalIgnoreCase);
    }

    private static float GetEmissionPhase(string materialName)
    {
        if (materialName.Contains("Orbit Pink", StringComparison.OrdinalIgnoreCase))
            return 2.10f;
        if (materialName.Contains("Orbit Violet", StringComparison.OrdinalIgnoreCase))
            return 4.20f;
        if (materialName.Contains("Detonation Orange", StringComparison.OrdinalIgnoreCase))
            return 5.05f;
        return 0f;
    }

    private static Node3D? FindNode3DByName(Node node, string expectedName)
    {
        if (node is Node3D node3D && node.Name.ToString() == expectedName)
            return node3D;

        foreach (var child in node.GetChildren())
        {
            if (FindNode3DByName(child, expectedName) is { } found)
                return found;
        }

        return null;
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player)
            return player;

        foreach (var child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found)
                return found;
        }
        return null;
    }
}

using Godot;
using DynamiteUniverse.Game;
using GodotEnvironment = Godot.Environment;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Mk VI 首页核心：透明 3D 子视口渲染 detonation-orrery-mk6.glb，再叠一层近/远双层彩色
/// additive 弥散光（canvas 着色器）。相机与灯光按独立预览对齐（FOV 32、18° 仰角、22° 方位角、
/// margin 0.90），不使用 Environment 默认 glow，也不做 Mk II 的材质名调优/脉冲灯。
/// 原 GLB 自带单个 4 秒动画：Full 原速循环、Reduced 降速并减弱弥散脉动、Off 停在稳定静帧。
/// </summary>
public partial class HomeEnergyReactor : Control
{
    private const string ModelPath = "res://assets/models/detonation-orrery-mk6.glb";
    private const string HaloShaderPath = "res://shaders/home_diffuse.gdshader";
    private const float CameraFovDegrees = 32f;
    private const float CameraElevationDegrees = 18f;
    private const float CameraAzimuthDegrees = 22f;
    private const float CameraMargin = 0.90f;

    /// <summary>客户端默认弥散强度：独立预览同值。</summary>
    private const float HaloGlow = 1.6f;
    private const float ReducedSpeed = 0.45f;
    private const float FullPulseAmplitude = 0.16f;
    private const float ReducedPulseAmplitude = 0.06f;

    private SubViewport? _viewport;
    private Camera3D? _camera;
    private AnimationPlayer? _motionPlayer;
    private ShaderMaterial? _haloMaterial;
    private string _clipName = "";
    private bool _staticFrameApplied;
    private double _pulseTime;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);

        var viewSize = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(Size.X)),
            Mathf.Max(1, Mathf.RoundToInt(Size.Y)));

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
            // The orbiter is one high-contrast element; keep AA local to this viewport.
            Msaa3D = Viewport.Msaa.Msaa4X,
            Size = viewSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(_viewport);

        var root = new Node3D { Name = "HomeEnergyReactorRoot" };
        _viewport.AddChild(root);
        root.AddChild(BuildEnvironment());
        root.AddChild(BuildCamera());
        BuildLights(root);

        var modelScene = GD.Load<PackedScene>(ModelPath);
        if (modelScene is null)
        {
            GD.PrintErr($"HomeEnergyReactor: failed to load {ModelPath}");
            return;
        }

        var model = modelScene.Instantiate<Node3D>();
        model.Name = "DetonationOrreryMk6";
        root.AddChild(model);

        BuildHalo();

        var player = FindAnimationPlayer(model);
        if (player is null)
        {
            GD.PrintErr("HomeEnergyReactor: GLB did not expose an AnimationPlayer");
            return;
        }

        var clips = player.GetAnimationList();
        if (clips.Length == 0)
        {
            GD.PrintErr("HomeEnergyReactor: GLB AnimationPlayer has no clips");
            return;
        }

        _clipName = clips[0];
        var clip = player.GetAnimation(_clipName);
        if (clip is not null)
            clip.LoopMode = Animation.LoopModeEnum.Linear;
        _motionPlayer = player;

        CallDeferred(nameof(DeferredFitCamera), model);
        ApplyMotionMode();
        UpdateGlow(0.0);
    }

    public override void _Process(double delta)
    {
        ApplyMotionMode();
        UpdateGlow(delta);
    }

    private WorldEnvironment BuildEnvironment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.035f, 0.105f, 0.24f),
                SkyHorizonColor = new Color(0.15f, 0.09f, 0.24f),
                GroundBottomColor = new Color(0.008f, 0.015f, 0.05f),
                GroundHorizonColor = new Color(0.045f, 0.11f, 0.23f),
                EnergyMultiplier = 0.80f,
            },
        };

        var environment = new GodotEnvironment
        {
            BackgroundMode = GodotEnvironment.BGMode.Color,
            BackgroundColor = new Color(0f, 0f, 0f, 0f),
            Sky = sky,
            ReflectedLightSource = GodotEnvironment.ReflectionSource.Sky,
            AmbientLightSource = GodotEnvironment.AmbientSource.Color,
            AmbientLightColor = new Color(0.42f, 0.58f, 0.86f),
            AmbientLightEnergy = 0.72f,
            TonemapMode = GodotEnvironment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            // 弥散光由 canvas 着色器负责，这里保持 Environment glow 关闭。
            GlowEnabled = false,
        };

        return new WorldEnvironment
        {
            Name = "ReactorEnvironment",
            Environment = environment,
        };
    }

    private Camera3D BuildCamera()
    {
        _camera = new Camera3D
        {
            Name = "ReactorCamera",
            Fov = CameraFovDegrees,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Current = true,
        };
        return _camera;
    }

    private static void BuildLights(Node3D root)
    {
        root.AddChild(new DirectionalLight3D
        {
            Name = "CyanKey",
            RotationDegrees = new Vector3(-42f, -30f, -15f),
            LightColor = new Color(0.35f, 0.80f, 1f),
            LightEnergy = 2.2f,
        });
        root.AddChild(new DirectionalLight3D
        {
            Name = "PinkRim",
            RotationDegrees = new Vector3(25f, 150f, 12f),
            LightColor = new Color(1f, 0.24f, 0.52f),
            LightEnergy = 1.5f,
        });
        root.AddChild(new DirectionalLight3D
        {
            Name = "BackSeparation",
            RotationDegrees = new Vector3(10f, 180f, 0f),
            LightColor = new Color(0.5f, 0.7f, 1f),
            LightEnergy = 0.9f,
        });
        root.AddChild(new OmniLight3D
        {
            Name = "ReadabilityFill",
            Position = new Vector3(0f, 2.5f, 10f),
            LightColor = new Color(0.80f, 0.89f, 1f),
            LightEnergy = 2.8f,
            OmniRange = 22f,
            ShadowEnabled = false,
        });
        root.AddChild(new OmniLight3D
        {
            Name = "CoreFill",
            Position = new Vector3(0f, 0.35f, 1.6f),
            LightColor = new Color(0.40f, 0.70f, 1f),
            LightEnergy = 1.8f,
            OmniRange = 12f,
            ShadowEnabled = false,
        });
    }

    private void BuildHalo()
    {
        if (_viewport is null)
            return;

        var halo = new TextureRect
        {
            Name = "DiffuseHalo",
            Size = Size,
            Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _haloMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(HaloShaderPath),
        };
        if (_haloMaterial.Shader is null)
        {
            GD.PrintErr($"HomeEnergyReactor: failed to load {HaloShaderPath}");
            return;
        }
        _haloMaterial.SetShaderParameter("u_glow", HaloGlow);
        _haloMaterial.SetShaderParameter("u_energy", 1.0f);
        halo.Material = _haloMaterial;
        AddChild(halo);
    }

    private void UpdateGlow(double delta)
    {
        if (_haloMaterial is null)
            return;

        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            SetGlowEnergy(1f);
            return;
        }

        var speed = profile.AllowLoop ? 1.0 : ReducedSpeed;
        var amplitude = profile.AllowLoop ? FullPulseAmplitude : ReducedPulseAmplitude;
        _pulseTime += delta * speed;
        SetGlowEnergy(1f + amplitude * Mathf.Sin((float)_pulseTime * Mathf.Tau * 3f / 4f));
    }

    private void SetGlowEnergy(float energy) =>
        _haloMaterial?.SetShaderParameter("u_energy", energy);

    private void ApplyMotionMode()
    {
        if (_motionPlayer is null || _clipName.Length == 0)
            return;

        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            if (_motionPlayer.IsPlaying())
                _motionPlayer.Stop();
            if (_staticFrameApplied)
                return;
            _motionPlayer.Seek(0.0, true);
            _staticFrameApplied = true;
            return;
        }

        _staticFrameApplied = false;
        var speed = profile.AllowLoop ? 1f : ReducedSpeed;
        if (!_motionPlayer.IsPlaying())
            _motionPlayer.Play(_clipName, -1.0, speed, false);
        else
            _motionPlayer.SpeedScale = speed;
    }

    private void DeferredFitCamera(Node3D model) => FitCameraToModel(model);

    private void FitCameraToModel(Node3D model)
    {
        if (_camera is null)
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
        var elevation = Mathf.DegToRad(CameraElevationDegrees);
        var azimuth = Mathf.DegToRad(CameraAzimuthDegrees);
        var halfVertical = 0.5f * (size.Y * Mathf.Cos(elevation) + size.Z * Mathf.Sin(elevation));
        var halfHorizontal = size.X * 0.5f;
        var aspect = _viewport is not null && _viewport.Size.Y > 0
            ? (float)_viewport.Size.X / _viewport.Size.Y
            : 1.087f;
        var verticalHalfFov = Mathf.DegToRad(CameraFovDegrees * 0.5f);
        var horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * aspect);
        var distance = Mathf.Max(
            halfVertical / Mathf.Tan(verticalHalfFov),
            halfHorizontal / Mathf.Tan(horizontalHalfFov)) * CameraMargin;
        var offset = new Vector3(
            Mathf.Sin(azimuth) * Mathf.Cos(elevation),
            Mathf.Sin(elevation),
            Mathf.Cos(azimuth) * Mathf.Cos(elevation)) * distance;

        var cameraPosition = center + offset;
        _camera.Position = cameraPosition;
        _camera.LookAtFromPosition(cameraPosition, center, Vector3.Up);
        _camera.Near = Mathf.Max(0.02f, distance * 0.01f);
        _camera.Far = distance + Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 2.5f;

        GD.Print($"HomeEnergyReactor: Mk VI AABB center={center}, size={size}, " +
            $"fitDistance={distance}, viewport={_viewport?.Size}");
    }

    private static Aabb ComputeAabb(Node3D node)
    {
        var aabb = new Aabb();
        var first = true;

        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var globalAabb = mesh.GlobalTransform * mesh.GetAabb();
                aabb = first ? globalAabb : aabb.Merge(globalAabb);
                first = false;
            }
            else if (child is Node3D child3d)
            {
                var childAabb = ComputeAabb(child3d);
                if (childAabb.Size.LengthSquared() <= 0.001f)
                    continue;
                aabb = first ? childAabb : aabb.Merge(childAabb);
                first = false;
            }
        }

        return aabb;
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

using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

/// <summary>Owns hit/sustain effect pools and release tails; never mutates judgement state.</summary>
internal sealed class GameplayEffects(Node2D noteRoot, GameSettings settings)
{
    private readonly Node2D _noteRoot = noteRoot;
    private readonly GameSettings _settings = settings;
    private readonly Dictionary<int, ContactVisual> _contacts = new();
    private sealed class ContactVisual
    {
        public required SustainPath Path;
        public GameplaySustainEffect? ContactEffect;
        public GameplaySustainParticles? ContactParticles;
    }

    internal void PrepareHits(int capacity)
    {
        for (var i = 0; i < capacity; i++)
        {
            var bloom = new GameplayHitBloom();
            _noteRoot.AddChild(bloom);
            bloom.ResetForPool();
            _bloomPool.Push(bloom);
            _hitParticlePool.Push(GameplayHitParticles.CreatePooled(_noteRoot));
        }
    }

    internal void PrepareSustains(int capacity, IEnumerable<SustainPath> paths)
    {
        foreach (var path in paths) _contacts.Add(path.HeadId, new() { Path = path });
        for (var i = 0; i < capacity; i++)
        {
            var effect = new GameplaySustainEffect { Visible = false };
            _noteRoot.AddChild(effect);
            _sustainEffectPool.Push(effect);
            _sustainParticlePool.Push(GameplaySustainParticles.CreatePooled(_noteRoot));
        }
    }
    private readonly List<GameplayHitBloom> _activeBlooms = new();
    private readonly Stack<GameplayHitBloom> _bloomPool = new();
    private readonly List<GameplayHitParticles> _activeHitParticles = new();
    private readonly Stack<GameplayHitParticles> _hitParticlePool = new();
    private readonly List<ContactVisual> _effectOwners = new();
    private readonly List<ContactVisual> _particleOwners = new();
    private readonly List<GameplaySustainParticles> _drainingParticles = new();
    private readonly Stack<GameplaySustainEffect> _sustainEffectPool = new();
    private readonly Stack<GameplaySustainParticles> _sustainParticlePool = new();

    internal void EmitBloom(Vector2 position, float rotation, Color accent,
        HitEffectKind kind, float span, float strength)
    {
        GameplayHitBloom bloom;
        if (_bloomPool.Count > 0)
            bloom = _bloomPool.Pop();
        else
        {
            bloom = _activeBlooms[0];
            _activeBlooms.RemoveAt(0);
        }
        bloom.ResetFor(position, rotation, accent, kind, span, strength);
        bloom.Intensity = _settings.FlashIntensityScale;
        bloom.ZIndex = 3;
        _noteRoot.MoveChild(bloom, -1);
        _activeBlooms.Add(bloom);
    }

    internal void RecycleExpired()
    {
        for (var i = _activeBlooms.Count - 1; i >= 0; i--)
        {
            var bloom = _activeBlooms[i];
            if (!bloom.IsExpired) continue;
            _activeBlooms.RemoveAt(i);
            bloom.ResetForPool();
            _bloomPool.Push(bloom);
        }
        for (var i = _activeHitParticles.Count - 1; i >= 0; i--)
        {
            var particles = _activeHitParticles[i];
            if (!particles.IsExpired) continue;
            _activeHitParticles.RemoveAt(i);
            particles.ResetForPool();
            _hitParticlePool.Push(particles);
        }
        for (var i = _drainingParticles.Count - 1; i >= 0; i--)
        {
            var particles = _drainingParticles[i];
            if (!particles.IsDrained) continue;
            _drainingParticles.RemoveAt(i);
            particles.ResetForPool();
            _sustainParticlePool.Push(particles);
        }
    }

    internal void UpdateSustainEffect(SustainPath path, bool connected,
        NoteBounds bounds, double position)
    {
        var state = _contacts[path.HeadId];
        if (_settings.HitEffectLevel == HitEffectLevel.Off)
        {
            ReleaseSustainEffect(state);
            return;
        }
        if (!connected)
        {
            state.ContactEffect?.SetContact(false);
            if (state.ContactParticles is { } retained)
            {
                retained.SetEmitting(false);
                retained.Visible = false;
            }
            return;
        }
        if (state.ContactEffect == null)
        {
            if (_sustainEffectPool.Count == 0)
            {
                var oldest = _effectOwners[0];
                ReleaseContactEffect(oldest);
            }
            state.ContactEffect = _sustainEffectPool.Pop();
            _effectOwners.Add(state);
            state.ContactEffect.ResetFor(state.Path.Kind == SustainKind.Hold
                ? SustainEffectKind.Hold : SustainEffectKind.Mixer,
                state.Path.Track == Track.Center ? 0f : Mathf.Pi * 0.5f);
        }
        state.ContactEffect.SetContact(true);
        state.ContactEffect.Position = TrackPositionAtLine(state.Path.Track, position);
        state.ContactEffect.SpanPx = SustainSpanPx(state.Path.Track, bounds);

        var particleMode = _settings.MotionMode;
        if (particleMode == UiMotionMode.Off)
            return;
        if (state.ContactParticles == null)
        {
            if (_sustainParticlePool.Count == 0)
            {
                if (_drainingParticles.Count > 0)
                {
                    var oldest = _drainingParticles[0];
                    _drainingParticles.RemoveAt(0);
                    oldest.ResetForPool();
                    _sustainParticlePool.Push(oldest);
                }
                else
                {
                    var oldest = _particleOwners[0];
                    var stolen = oldest.ContactParticles!;
                    oldest.ContactParticles = null;
                    _particleOwners.RemoveAt(0);
                    stolen.ResetForPool();
                    _sustainParticlePool.Push(stolen);
                }
            }
            state.ContactParticles = _sustainParticlePool.Pop();
            _particleOwners.Add(state);
            state.ContactParticles.ResetFor(GameplayMirror.DisplayTrack(state.Path.Track) switch
            {
                Track.Center => 0f,
                Track.Left => Mathf.Pi * 0.5f,
                _ => -Mathf.Pi * 0.5f,
            }, state.Path.Kind == SustainKind.Hold ? HitEffectKind.Hold : HitEffectKind.Mixer,
                particleMode, SustainSpanPx(state.Path.Track, bounds));
        }
        state.ContactParticles.Visible = true;
        state.ContactParticles.SetEmitting(true);
        state.ContactParticles.Position = TrackPositionAtLine(state.Path.Track, position);
    }

    private void ReleaseContactEffect(ContactVisual state)
    {
        if (state.ContactEffect == null) return;
        _effectOwners.Remove(state);
        state.ContactEffect.ResetForPool();
        _sustainEffectPool.Push(state.ContactEffect);
        state.ContactEffect = null;
    }

    private void ReleaseSustainEffect(ContactVisual state)
    {
        ReleaseContactEffect(state);
        if (state.ContactParticles == null) return;
        _particleOwners.Remove(state);
        state.ContactParticles.BeginRelease();
        _drainingParticles.Add(state.ContactParticles);
        state.ContactParticles = null;
    }

    internal void ReleaseSustainEffect(int headId) => ReleaseSustainEffect(_contacts[headId]);

    internal void EmitParticles(Vector2 position, float rotation, Color accent,
        HitEffectKind kind, float strength, UiMotionMode mode)
    {
        GameplayHitParticles particles;
        if (_hitParticlePool.Count > 0)
            particles = _hitParticlePool.Pop();
        else
        {
            particles = _activeHitParticles[0];
            _activeHitParticles.RemoveAt(0);
        }
        particles.ResetFor(position, rotation, accent, kind, strength, mode);
        particles.GetParent().MoveChild(particles, -1);
        _activeHitParticles.Add(particles);
    }
}

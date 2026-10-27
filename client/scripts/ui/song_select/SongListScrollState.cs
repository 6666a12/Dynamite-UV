namespace DynamiteUniverse.Ui;

internal sealed class SongListScrollState
{
    public bool Animating { get; private set; }
    public float Display { get; private set; }
    public float Target { get; private set; }
    public int Written { get; private set; }

    public bool Push(float actual, float maximum, float pixels, double tau)
    {
        if (!Animating)
        {
            Display = actual;
            Target = actual;
            Written = (int)actual;
        }

        Target = Math.Clamp(Target + pixels, 0f, maximum);
        if (tau <= 0d)
        {
            Display = Target;
            Animating = false;
            return true;
        }

        Animating = true;
        return false;
    }

    public bool Step(int actual, double tau, double delta)
    {
        if (actual != Written)
        {
            Display = actual;
            Target = actual;
            Written = actual;
            Animating = false;
            return false;
        }
        if (!Animating)
            return false;

        var reached = Math.Abs(Target - Display) <= ScrollInertia.SnapPixels;
        Display = reached ? Target : ScrollInertia.Step(Display, Target, tau, delta);
        Animating = !reached;
        return true;
    }

    public void AcceptWrite(int wanted, int landed)
    {
        if (landed != wanted)
        {
            Display = landed;
            Target = landed;
            Animating = false;
        }
        Written = landed;
    }

    public void Stop(int actual)
    {
        Animating = false;
        Display = actual;
        Target = actual;
        Written = actual;
    }
}

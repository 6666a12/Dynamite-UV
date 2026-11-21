using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Deliver the intent to the one account scene reached by this accepted navigation.
/// No global 'return to store' flag for a later/direct account visit to accidentally consume.</summary>
internal static class HomeStoreRoute
{
    public static bool Open(Node home)
    {
        var state = OnlineSession.Connection.State;
        if (state.Account is not null)
        {
            return TransitionDirector.Navigate(UiRoutes.Store, TransitionKind.Standard, "OPEN CHANNEL", "CHART STORE");
        }
        var intent = new StoreLoginIntent(state);
        var tree = home.GetTree();
        return TransitionDirector.Navigate(UiRoutes.Online, TransitionKind.Standard, "CONNECT", "ACCOUNT", beforeSwitch: () =>
        {
            // ChangeSceneToFile detaches the previous scene immediately on success, then adds the
            // incoming scene and emits SceneChanged after it is ready. On failure home stays current.
            var outgoing = tree.CurrentScene;
            void Deliver()
            {
                tree.SceneChanged -= Deliver;
                if (tree.CurrentScene is OnlineScreen account && tree.CurrentScene.SceneFilePath == UiRoutes.Online)
                    account.ContinueToStore(intent);
            }
            tree.SceneChanged += Deliver;
            Callable.From(() => { if (tree.CurrentScene == outgoing) tree.SceneChanged -= Deliver; }).CallDeferred();
        });
    }
}

#if TOOLS
using Godot;
using System;

namespace WorkTimeCounter
{
    [Tool]
    public class WorkTimerPlugin : EditorPlugin
    {
        WorkTimer node = null;
        ExportCounter exportCounter = null;
        public const string PluginDir = "res://addons/WorkTimer/";
        public static WorkTimerPlugin Instance { get; private set; } = null;

        #region Init and Deinit everything

        bool isDisabling = true;

        WorkTimerPlugin()
        {
            Instance = this;
        }

        public override void _EnterTree()
        {
            isDisabling = false;
            Init();
            var editor = GetEditorNode();
            if (editor != null)
            {
                editor.Connect("play_pressed", this, nameof(PlayPressed));
            }

            exportCounter = new ExportCounter();
            AddExportPlugin(exportCounter);
        }

        public override void _ExitTree()
        {
            isDisabling = true;
            Deinit();
            RemoveExportPlugin(exportCounter);
        }

        protected override void Dispose(bool disposing)
        {
            Deinit();

            if (!isDisabling)
                CallDeferred(nameof(Init));

            base.Dispose(disposing);
        }

        void Init()
        {
            node?.QueueFree();
            node = new WorkTimer();
            AddControlToContainer(CustomControlContainer.Toolbar, node);
        }

        void Deinit()
        {
            Instance = null;
            if (node != null && node.NativeInstance != IntPtr.Zero)
            {
                RemoveControlFromContainer(CustomControlContainer.Toolbar, node);
                //node.QueueFree();
                node.Free();
            }
            node = null;
        }

        Node GetEditorNode()
        {
            var ch = GetTree().Root.GetChildren();
            foreach (Node c in ch)
            {
                if (c.GetClass() == "EditorNode")
                    return c;
            }
            return null;
        }

        #endregion Init and Deinit everything

        public void PlayPressed()
        {
            node.Counter.IncrementPlays();
            node.UpdateTime();
        }

        public void ExportPressed()
        {
            node.Counter.IncrementExports();
            node.UpdateTime();
        }
    }

    class ExportCounter : EditorExportPlugin
    {
        public override void _ExportBegin(string[] features, bool isDebug, string path, int flags)
        {
            WorkTimerPlugin.Instance?.ExportPressed();
        }
    }
}
#endif
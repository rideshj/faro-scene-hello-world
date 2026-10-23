using System;

namespace FAROSceneSDCardWorkflow.Workflow
{
    public enum WorkflowStage
    {
        Idle,
        CardDetected,
        ProjectCreated,
        ImportingScans,
        ProcessingScans,
        RegisteringTargets,
        VerifyingTopView,
        Completed,
        Cancelled,
        Failed
    }

    public class WorkflowProgressEventArgs : EventArgs
    {
        public WorkflowStage Stage { get; set; }
        public string Message { get; set; }
        public int Percentage { get; set; }

        public WorkflowProgressEventArgs(WorkflowStage stage, string message, int pct)
        {
            Stage = stage;
            Message = message;
            Percentage = pct;
        }
    }
}

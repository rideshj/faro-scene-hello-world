using System;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneHelloWorld.UI;

namespace FAROSceneHelloWorld.Commands
{
    /// <summary>
    /// Command handler for Ribbon button "Auto-Classify Elements".
    /// Launches the classification controller dialog to segment Floors, Ceilings, Walls, Columns, and Pipes.
    /// </summary>
    public class ClassifyElementsCommand
    {
        private readonly IAppHost _host;

        public ClassifyElementsCommand(IAppHost host)
        {
            _host = host;
        }

        public void Execute(object sender = null, EventArgs e = null)
        {
            try
            {
                _host?.Logger?.LogInfo("[FAROSceneHelloWorld] ClassifyElementsCommand invoked.");

                using (var dialog = new ClassificationProgressDialog(_host))
                {
                    dialog.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                _host?.Logger?.LogError($"[FAROSceneHelloWorld] Error launching classification dialog: {ex.Message}", ex);
                MessageBox.Show(
                    "Error launching Auto-Classifier:\n\n" + ex.Message,
                    "FARO SCENE - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}

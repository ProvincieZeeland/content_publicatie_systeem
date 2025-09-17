using System;
using System.Windows.Forms;
using CPS_ManagementTool;

namespace JwtWithPfx
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new StartupForm());
        }
    }
    public static class StartupFormConstants
    {
        public const string Title = "CPS Management Tool";
        public const string JwtTokenGenerationButtonLabel = "Generate JWT token for Postman";
    }

    public class StartupForm : Form
    {
        public StartupForm()
        {
            Text = StartupFormConstants.Title;
            Width = 320;
            Height = 150;
            var btn = new Button()
            {
                Text = StartupFormConstants.JwtTokenGenerationButtonLabel,
                Width = 250,
                Height = 40,
                Left = 30,
                Top = 30
            };
            btn.Click += (s, e) =>
            {
                Hide();
                var jwtForm = new JwtTokenGenerationForm();
                jwtForm.StartPosition = FormStartPosition.CenterScreen;
                jwtForm.FormClosed += (sender, args) => Close();
                jwtForm.Show();
            };
            Controls.Add(btn);
            StartPosition = FormStartPosition.CenterScreen;
        }
    }
}
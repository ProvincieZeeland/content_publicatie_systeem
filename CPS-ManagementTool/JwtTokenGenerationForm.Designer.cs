using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using JwtWithPfx;

namespace CPS_ManagementTool
{
    public static class JwtTokenGenerationFormConstants
    {
        public const string BtnSelectPfxName = "btnSelectPfx";
        public const string BtnSelectPfxText = "Select PFX and Generate JWT";
        public const string txtJwtName = "txtJwt";
        public const string BtnBackName = "btnBack";
        public const string BtnBackText = "←  Back";
        public const string JwtTokenGenerationFormName = "JwtTokenGenerationForm";
        public const string JwtTokenGenerationFormFormTitle = "JWT Generator with PFX";
        public const string OpenFileDialogFilter = "PFX files (*.pfx)|*.pfx";
        public const string OpenFileDialogTitle = "Select a PFX Certificate";
        public const string ErrorTitle = "Error";
        public const string AudTitle = "aud";
        public const string AudValue = "https://login.microsoftonline.com/76850799-28ea-4f56-b80d-c1640687052d/oauth2/v2.0/token";
        public const string ExpTitle = "exp";
        public const string NbfTitle = "nbf";
        public const string IssTitle = "iss";
        public const string SubTitle = "sub";
        public const string JtiTitle = "jti";
        public const string X5tTitle = "x5t";
        public const string PasswordPromptTitle = "Enter PFX Password and Client ID";
        public const string PasswordLabelText = "PFX Password:";
        public const string ClientIdLabelText = "Client ID:";
        public const string OkButtonText = "Ok";
        public const string CancelButtonText = "Cancel";
    }

    partial class JwtTokenGenerationForm
    {
        private System.Windows.Forms.Button btnSelectPfx;
        private System.Windows.Forms.TextBox txtJwt;

        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnSelectPfx = new System.Windows.Forms.Button();
            this.txtJwt = new System.Windows.Forms.TextBox();
            this.btnBack = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnSelectPfx
            // 
            this.btnSelectPfx.Location = new System.Drawing.Point(30, 20);
            this.btnSelectPfx.Name = JwtTokenGenerationFormConstants.BtnSelectPfxName;
            this.btnSelectPfx.Size = new System.Drawing.Size(200, 30);
            this.btnSelectPfx.TabIndex = 0;
            this.btnSelectPfx.Text = JwtTokenGenerationFormConstants.BtnSelectPfxText;
            this.btnSelectPfx.UseVisualStyleBackColor = true;
            this.btnSelectPfx.Click += new System.EventHandler(this.btnSelectPfx_Click);
            // 
            // txtJwt
            // 
            this.txtJwt.Location = new System.Drawing.Point(30, 70);
            this.txtJwt.Multiline = true;
            this.txtJwt.Name = JwtTokenGenerationFormConstants.txtJwtName;
            this.txtJwt.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtJwt.Size = new System.Drawing.Size(663, 263);
            this.txtJwt.TabIndex = 1;
            // 
            // btnBack
            // 
            this.btnBack.Location = new System.Drawing.Point(30, 355);
            this.btnBack.Name = JwtTokenGenerationFormConstants.BtnBackName;
            this.btnBack.Size = new System.Drawing.Size(84, 30);
            this.btnBack.TabIndex = 2;
            this.btnBack.Text = JwtTokenGenerationFormConstants.BtnBackText;
            this.btnBack.UseVisualStyleBackColor = true;
            this.btnBack.Click += new System.EventHandler(this.btnBack_Click);
            // 
            // JwtTokenGenerationForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(724, 405);
            this.Controls.Add(this.btnBack);
            this.Controls.Add(this.txtJwt);
            this.Controls.Add(this.btnSelectPfx);
            this.Name = JwtTokenGenerationFormConstants.JwtTokenGenerationFormName;
            this.Text = JwtTokenGenerationFormConstants.JwtTokenGenerationFormFormTitle;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void btnSelectPfx_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = JwtTokenGenerationFormConstants.OpenFileDialogFilter;
                openFileDialog.Title = JwtTokenGenerationFormConstants.OpenFileDialogTitle;

                if (openFileDialog.ShowDialog() != DialogResult.OK) return;

                (string password, string clientId) = Prompt.ShowPasswordAndClientIdDialog();
                if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(clientId))
                {
                    try
                    {
                        var cert = new X509Certificate2(openFileDialog.FileName, password, X509KeyStorageFlags.Exportable);
                        txtJwt.Text = CreateJwt(cert, clientId);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"{JwtTokenGenerationFormConstants.ErrorTitle}: {ex.Message}", JwtTokenGenerationFormConstants.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void btnBack_Click(object sender, System.EventArgs e)
        {
            Hide();
            var startupForm = new StartupForm();
            var dialogResult = startupForm.ShowDialog();
        }

        private string CreateJwt(X509Certificate2 cert, string clientId)
        {
            var expiration = DateTimeOffset.Now.AddMonths(1).ToUnixTimeSeconds();

            var securityKey = new X509SecurityKey(cert);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
            var handler = new JwtSecurityTokenHandler();
            JwtSecurityToken token = handler.CreateJwtSecurityToken(
                subject: new ClaimsIdentity(new[] { 
                    new Claim(JwtTokenGenerationFormConstants.AudTitle, JwtTokenGenerationFormConstants.AudValue), 
                    new Claim(JwtTokenGenerationFormConstants.ExpTitle, expiration.ToString(), System.Security.Claims.ClaimValueTypes.Integer32), 
                    new Claim(JwtTokenGenerationFormConstants.NbfTitle, expiration.ToString(), System.Security.Claims.ClaimValueTypes.Integer32), 
                    new Claim(JwtTokenGenerationFormConstants.IssTitle, clientId), 
                    new Claim(JwtTokenGenerationFormConstants.SubTitle, clientId),
                    new Claim(JwtTokenGenerationFormConstants.JtiTitle, new Guid().ToString())
                }),
                signingCredentials: credentials
            );

            // Add x5t header (base64url encoded thumbprint)
            token.Header[JwtTokenGenerationFormConstants.X5tTitle] = Base64UrlEncoder.Encode(cert.GetCertHash());

            return handler.WriteToken(token);
        }

        private Button btnBack;
    }

    public static class Prompt
    {
        public static (string password, string clientId) ShowPasswordAndClientIdDialog()
        {
            Form prompt = new Form()
            {
                Width = 500,
                Height = 220,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = JwtTokenGenerationFormConstants.PasswordPromptTitle,
                StartPosition = FormStartPosition.CenterScreen
            };
            Label passwordLabel = new Label() { Left = 50, Top = 20, Text = JwtTokenGenerationFormConstants.PasswordLabelText, Width = 120 };
            TextBox passwordBox = new TextBox() { Left = 180, Top = 20, Width = 250, PasswordChar = '*' };
            Label clientIdLabel = new Label() { Left = 50, Top = 60, Text = JwtTokenGenerationFormConstants.ClientIdLabelText, Width = 120 };
            TextBox clientIdBox = new TextBox() { Left = 180, Top = 60, Width = 250 };
            Button confirmation = new Button() { Text = JwtTokenGenerationFormConstants.OkButtonText, Left = 250, Width = 80, Top = 120, DialogResult = DialogResult.OK };
            Button cancel = new Button() { Text = JwtTokenGenerationFormConstants.CancelButtonText, Left = 350, Width = 80, Top = 120, DialogResult = DialogResult.Cancel };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            cancel.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(passwordLabel);
            prompt.Controls.Add(passwordBox);
            prompt.Controls.Add(clientIdLabel);
            prompt.Controls.Add(clientIdBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(cancel);
            prompt.AcceptButton = confirmation;
            prompt.CancelButton = cancel;

            var dialogResult = prompt.ShowDialog();
            if (dialogResult == DialogResult.OK)  return (passwordBox.Text, clientIdBox.Text);
            else return (null, null);
        }
    }
}


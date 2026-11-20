// MSFSBlindAssist/Forms/PlannedRoutePopupForm.cs
using System;
using System.Drawing;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Accessible popup dialog notifying the pilot that an arrival taxi route to the gate has been planned.
/// Offers options to view the planned route in Taxi Guidance or acknowledge.
/// </summary>
public class PlannedRoutePopupForm : Form
{
    private readonly string _message;
    private readonly Action? _onViewPlannedRoute;
    private readonly ScreenReaderAnnouncer? _announcer;

    private Label lblMessage = null!;
    private Button btnView = null!;
    private Button btnOk = null!;

    public PlannedRoutePopupForm(string message, Action? onViewPlannedRoute = null, ScreenReaderAnnouncer? announcer = null)
    {
        _message = message;
        _onViewPlannedRoute = onViewPlannedRoute;
        _announcer = announcer;
        InitializeFormControls();
    }

    private void InitializeFormControls()
    {
        this.Text = "Planned Taxi Route";
        this.Size = new Size(480, 220);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;

        lblMessage = new Label
        {
            Text = _message,
            Location = new Point(20, 20),
            Size = new Size(420, 80),
            AccessibleName = "Planned Route Notice",
            AccessibleDescription = _message
        };

        btnView = new Button
        {
            Text = "&View planned route",
            Location = new Point(20, 120),
            Size = new Size(180, 32),
            AccessibleName = "View planned route",
            AccessibleDescription = "Opens the taxi guidance window to view the planned route in read-only mode."
        };
        btnView.Click += (s, e) =>
        {
            this.DialogResult = DialogResult.Yes;
            this.Close();
            _onViewPlannedRoute?.Invoke();
        };

        btnOk = new Button
        {
            Text = "&OK",
            Location = new Point(220, 120),
            Size = new Size(120, 32),
            AccessibleName = "OK",
            AccessibleDescription = "Acknowledge and close notification."
        };
        btnOk.Click += (s, e) =>
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        };

        this.AcceptButton = btnOk;
        this.CancelButton = btnOk;

        this.Controls.Add(lblMessage);
        this.Controls.Add(btnView);
        this.Controls.Add(btnOk);

        int t = 0;
        lblMessage.TabIndex = t++;
        btnView.TabIndex = t++;
        btnOk.TabIndex = t++;

        this.Load += (s, e) =>
        {
            _announcer?.Announce(_message);
            btnOk.Focus();
        };
    }
}


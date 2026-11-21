using System.Collections.Generic;
using System.Linq;

using Godot;
using Otchlan.Core;
using Otchlan.Input;
using Otchlan.Save;
using Otchlan.Sim;
using Otchlan.View;

namespace Otchlan.UI;

/// <summary>
/// Ekran „Osiągnięcia”: siatka kart (ikona, nazwa, opis, postęp albo „Odblokowane”). Ukryte, jeszcze nieodblokowane
/// osiągnięcia pokazują „???”. Przewijanie klawiaturą/padem (fokus kart), myszą (kółko); Cancel = wstecz.
/// </summary>
public partial class AchievementsScreen : MenuScreen
{
    private const int Columns = 3;
    private readonly Profile _prof;

    public AchievementsScreen() : this(Profile.P) { }
    public AchievementsScreen(Profile profile) => _prof = profile;

    protected override string Header => "Osiągnięcia";
    protected override string Sub
    {
        get
        {
            var all = Content.Db.Achievements;
            int done = all.Count(a => _prof.Achievements.GetValueOrDefault(a.Api));
            return $"Odblokowane: {done} / {all.Count}";
        }
    }
    protected override (string action, string text)[] Prompts => new[] { (Act.Cancel, "Wróć") };
    protected override void OnBack() => Game.I.ShowScreen(new MainMenu());

    protected override void Build(VBoxContainer body)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        body.AddChild(scroll);
        var grid = new GridContainer { Columns = Columns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        scroll.AddChild(grid);

        foreach (var def in Content.Db.Achievements)
        {
            var card = MakeCard(def);
            grid.AddChild(card);
            FocusTarget ??= card;
        }
    }

    private Button MakeCard(AchievementDef def)
    {
        bool done = _prof.Achievements.GetValueOrDefault(def.Api);
        bool secret = def.Hidden && !done;
        var tint = AchievementIcons.Tint(def);
        float? goal = AchievementTracker.GoalOf(def);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.MouseFilter = MouseFilterEnum.Ignore;

        var iconBox = new PanelContainer { CustomMinimumSize = new Vector2(92, 92), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        var iconBg = UiTheme.Box(done ? new Color(tint.R * 0.35f, tint.G * 0.35f, tint.B * 0.35f) : new Color("1f1830"), 14, 3, 0,
            done ? tint : new Color("3a2c57"));
        iconBox.AddThemeStyleboxOverride("panel", iconBg);
        var tex = secret ? Art.Ui("lock") : AchievementIcons.Texture(def);
        var icon = Ui.Icon(tex, 76, done ? Colors.White : new Color(1, 1, 1, secret ? 0.5f : 0.35f));
        iconBox.AddChild(icon);
        row.AddChild(iconBox);

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        col.AddChild(Ui.Lbl(secret ? "???" : def.Name, 30, done ? UiTheme.Text : UiTheme.TextDim));
        var desc = Ui.Lbl(secret ? "Ukryte osiągnięcie." : def.Desc, 20, UiTheme.TextDim, wrap: true);
        desc.CustomMinimumSize = new Vector2(0, 70);
        col.AddChild(desc);
        col.AddChild(StatusLine(def, done, secret, goal, tint));
        row.AddChild(col);

        var normal = UiTheme.Box(done ? new Color("3a2c57") : new Color("2b2140"), 18, 4, 4, done ? tint : null);
        var hover = UiTheme.Box(new Color("4a3a6e"), 18, 4, 4, UiTheme.Accent);
        var card = CardButton(new Vector2(0, 172), row, normal, hover);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.ClipText = false;
        return card;
    }

    private Control StatusLine(AchievementDef def, bool done, bool secret, float? goal, Color tint)
    {
        if (done)
        {
            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            h.AddThemeConstantOverride("separation", 8);
            h.AddChild(Ui.Icon(Art.Ui("check"), 24, UiTheme.Good));
            h.AddChild(Ui.Lbl("Odblokowane", 20, UiTheme.Good));
            return h;
        }
        if (goal is > 0 && !secret)
        {
            float cur = AchievementTracker.ProgressOf(_prof, def);
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            v.AddThemeConstantOverride("separation", 2);
            var bar = new ProgressBar
            {
                MinValue = 0, MaxValue = goal.Value, Value = cur, ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 14), MouseFilter = MouseFilterEnum.Ignore,
            };
            bar.AddThemeStyleboxOverride("background", UiTheme.Box(new Color("1a0f1f"), 7, 2, 0, new Color("3a2c57")));
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = tint, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7, CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7 });
            v.AddChild(bar);
            v.AddChild(Ui.Lbl($"{(int)cur} / {(int)goal.Value}", 18, UiTheme.TextDim));
            return v;
        }
        return Ui.Lbl("Zablokowane", 20, UiTheme.TextDim);
    }
}

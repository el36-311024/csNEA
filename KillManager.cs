using Godot;

public partial class KillManager : Node
{
	public static KillManager Instance;
	
	public int TeamKills;
	public int EnemyKills;
	
	private ProgressBar TeamBar;
	private ProgressBar EnemyBar;

	private Label TeamCount;
	private Label EnemyCount;

	private const int MaxKills = 50;

	public override void _Ready()
	{
		Instance = this;
	}

	public void RegisterUI(ProgressBar teamBar, ProgressBar enemyBar, Label teamCount, Label enemyCount)
	{
		TeamBar = teamBar;
		EnemyBar = enemyBar;
		TeamCount = teamCount;
		EnemyCount = enemyCount;

		TeamBar.MaxValue = MaxKills;
		EnemyBar.MaxValue = MaxKills;

		UpdateUI();
	}

	private void UpdateUI()
	{
		if (TeamBar != null)
			TeamBar.Value = TeamKills;

		if (EnemyBar != null)
			EnemyBar.Value = EnemyKills;

		if (TeamCount != null)
			TeamCount.Text = TeamKills.ToString();

		if (EnemyCount != null)
			EnemyCount.Text = EnemyKills.ToString();
	}

	public void AddTeamKill()
	{
		TeamKills++;
		UpdateUI();
		CheckWin();
	}

	public void AddEnemyKill()
	{
		EnemyKills++;
		UpdateUI();
		CheckWin();
	}

	private void CheckWin()
	{
		if (TeamKills >= MaxKills)
		{
			MatchStats.Instance.TeamWon = true;
			MatchStats.Instance.WinByCapture = false;
			GetTree().ChangeSceneToFile("res://EndGame.tscn");
		}

		if (EnemyKills >= MaxKills)
		{
			MatchStats.Instance.TeamWon = false;
			MatchStats.Instance.WinByCapture = false;
			GetTree().ChangeSceneToFile("res://EndGame.tscn");
		}
	}
}

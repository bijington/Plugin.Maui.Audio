namespace Plugin.Maui.Audio;

partial class AudioRecorder
{
	CancellationTokenSource stopCancelTokenSource = new();

	public bool SoundDetected { get; set; }

	async ValueTask<bool> CheckStopRuleAsync(IStopRule? stopRule, CancellationToken cancellationToken)
	{
		if (cancellationToken == default)
		{
			cancellationToken = stopCancelTokenSource.Token;
		}

		bool isStopRuleFulfilled = await (stopRule ?? When.Immediately()).EnforceStop(this, cancellationToken);

		if (isStopRuleFulfilled)
		{
			stopCancelTokenSource.Cancel();
			stopCancelTokenSource = new();
		}

		return isStopRuleFulfilled;
	}
}

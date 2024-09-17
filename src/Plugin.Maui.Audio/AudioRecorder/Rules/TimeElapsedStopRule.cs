using System.Diagnostics;

namespace Plugin.Maui.Audio;

class TimeElapsedStopRule : IStopRule
{
	readonly TimeSpan recordDuration;

	internal TimeElapsedStopRule(TimeSpan recordDuration)
	{
		this.recordDuration = recordDuration;
	}
	
	public async ValueTask<bool> EnforceStop(IAudioRecorder recorder, CancellationToken cancellationToken = default)
	{
		return await TimeElapsedAsync(recorder, cancellationToken);
	}
	
	async Task<bool> TimeElapsedAsync(IAudioRecorder recorder, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(recordDuration, TimeSpan.FromSeconds(0));

		DateTime recordStartTime = DateTime.UtcNow;
		bool timeElapsed = false;

		await Task.Run(() =>
		{
			while (recorder.IsRecording)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					Debug.WriteLine("Stop record when time will elapse task has been canceled.");
					break;
				}

				if (DateTime.UtcNow - recordStartTime > recordDuration)
				{
					timeElapsed = true;
					break;
				}
			}
		}, cancellationToken);

		return timeElapsed;
	}
}

partial class When
{
	public static IStopRule TimeElapsed(TimeSpan duration) => new TimeElapsedStopRule(duration);
}
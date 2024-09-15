namespace Plugin.Maui.Audio;

public interface IStopRule
{
	ValueTask<bool> EnforceStop(IAudioRecorder recorder, CancellationToken cancelToken = default);
}
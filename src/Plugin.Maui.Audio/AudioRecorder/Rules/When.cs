namespace Plugin.Maui.Audio;

public static class When
{
	public static IStopRule Immediately() => new ImmediateStopRule();
	
	public static IStopRule SilenceIsDetected(double thresholdOf, int forDuration) => new SilenceIsDetectedStopRule(thresholdOf, forDuration);
}
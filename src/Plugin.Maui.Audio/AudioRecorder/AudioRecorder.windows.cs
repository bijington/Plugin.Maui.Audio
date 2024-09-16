﻿using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Plugin.Maui.Audio;

partial class AudioRecorder : IAudioRecorder
{
	MediaCapture? mediaCapture;
	string audioFilePath = string.Empty;
	StorageFile? fileOnDisk;

	uint sampleRate;
	uint channelCount;
	uint bitsPerSample;

	FileStream? audioFileStream;
	long startingAudioFileStreamLength;
	int audioChunkNumber;

	public bool CanRecordAudio { get; private set; } = true;
	public bool IsRecording => mediaCapture != null;

	readonly AudioRecorderOptions options;

	public AudioRecorder(AudioRecorderOptions options)
	{
		this.options = options;
	}

	public Task StartAsync() => StartAsync(DefaultAudioRecordingOptions.DefaultOptions);

	public Task StartAsync(string filePath) => StartAsync(filePath, DefaultAudioRecordingOptions.DefaultOptions);

	public async Task StartAsync(AudioRecordingOptions options)
	{
		var localFolder = ApplicationData.Current.LocalFolder;
		var fileName = Path.GetRandomFileName();

		fileOnDisk = await localFolder.CreateFileAsync(fileName);

		await StartAsync(fileOnDisk.Path, options);
	}

	public async Task StartAsync(string filePath, AudioRecordingOptions options)
	{
		if (mediaCapture is not null)
		{
			throw new InvalidOperationException("Recording already in progress");
		}

		try
		{
			var captureSettings = new MediaCaptureInitializationSettings()
			{
				StreamingCaptureMode = StreamingCaptureMode.Audio
			};
			await InitMediaCapture(captureSettings);
		}
		catch (Exception ex)
		{
			CanRecordAudio = false;
			DeleteMediaCapture();

			if (ex.InnerException != null && ex.InnerException.GetType() == typeof(UnauthorizedAccessException))
			{
				throw ex.InnerException;
			}
			throw;
		}

		var fileOnDisk = await StorageFile.GetFileFromPathAsync(filePath);

		try
		{
			try
			{
				var profile = SharedOptionsToWindowsMediaProfile(options);
				await mediaCapture?.StartRecordToStorageFileAsync(profile, fileOnDisk);
				SoundDetected = false;
			}
			catch
			{
				if(options.ThrowIfNotSupported)
				{
					throw;
				}

				var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Auto);

				sampleRate =  (uint)DefaultAudioRecordingOptions.DefaultOptions.SampleRate;
				channelCount = (uint)DefaultAudioRecordingOptions.DefaultOptions.Channels;
				bitsPerSample = (uint)DefaultAudioRecordingOptions.DefaultOptions.BitDepth;

				profile.Audio = AudioEncodingProperties.CreatePcm(sampleRate, channelCount, bitsPerSample);

				await mediaCapture?.StartRecordToStorageFileAsync(profile, fileOnDisk);
			}
		}
		catch
		{
			CanRecordAudio = false;
			DeleteMediaCapture();
			throw;
		}

		audioFilePath = fileOnDisk.Path;

		audioFileStream = GetFileStream();
	}

	MediaEncodingProfile SharedOptionsToWindowsMediaProfile(AudioRecordingOptions options)
	{
		sampleRate = (uint)options.SampleRate;
		channelCount = (uint)options.Channels;
		bitsPerSample = (uint)options.BitDepth;

		switch (options.Encoding)
		{
			case Encoding.LinearPCM:
				var profilePCM = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Auto);
				profilePCM.Audio = AudioEncodingProperties.CreatePcm(sampleRate, channelCount, bitsPerSample);
				return profilePCM;
			case Encoding.Flac:
				var profileFlac = MediaEncodingProfile.CreateFlac(AudioEncodingQuality.Auto);
				profileFlac.Audio = AudioEncodingProperties.CreateFlac(sampleRate, channelCount, bitsPerSample);
				return profileFlac;
			case Encoding.Alac:
				var profileAlac = MediaEncodingProfile.CreateAlac(AudioEncodingQuality.Auto);
				profileAlac.Audio = AudioEncodingProperties.CreateAlac(sampleRate, channelCount, bitsPerSample);
				return profileAlac;
			default:
				throw new NotSupportedException("Encoding not supported");
		}
	}

	async Task InitMediaCapture(MediaCaptureInitializationSettings settings)
	{
		mediaCapture = new MediaCapture();

		await mediaCapture.InitializeAsync(settings);

		mediaCapture.RecordLimitationExceeded += (MediaCapture sender) =>
		{
			CanRecordAudio = false;
			DeleteMediaCapture();
			throw new Exception("Record Limitation Exceeded");
		};

		mediaCapture.Failed += (MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs) =>
		{
			CanRecordAudio = false;
			DeleteMediaCapture();
			throw new Exception(string.Format("Code: {0}. {1}", errorEventArgs.Code, errorEventArgs.Message));
		};
	}

	public async Task<IAudioSource> StopAsync(IStopRule? stopRule = null, CancellationToken cancellationToken = default)
	{
		if (mediaCapture == null)
		{
			throw new InvalidOperationException("No recording in progress");
		}
		
		bool isStopRuleFulfilled = await CheckStopRuleAsync(stopRule, cancellationToken);

		if (isStopRuleFulfilled)
		{
			await mediaCapture.StopRecordAsync();

			mediaCapture.Dispose();
			mediaCapture = null;

			audioFileStream?.Dispose();
		}

		return GetRecording();
	}

	IAudioSource GetRecording()
	{
		if (File.Exists(audioFilePath))
		{
			return new FileAudioSource(audioFilePath);
		}

		return new EmptyAudioSource();
	}

	void DeleteMediaCapture()
	{
		try
		{
			mediaCapture?.Dispose();
		}
		catch
		{
			//ignore
		}

		try
		{
			if (!string.IsNullOrWhiteSpace(audioFilePath) && File.Exists(audioFilePath))
			{
				File.Delete(audioFilePath);
			}
		}
		catch
		{
			//ignore
		}

        audioFilePath = string.Empty;
        mediaCapture = null;
    }

	FileStream GetFileStream()
	{
		int wavFileHeaderLength = 44;

		FileStream fileStream = new(audioFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		startingAudioFileStreamLength = fileStream.Length;

		if (startingAudioFileStreamLength == 0)
		{
			startingAudioFileStreamLength = wavFileHeaderLength;
		}

		audioChunkNumber = 1;

		return fileStream;
	}

	public byte[]? GetAudioDataChunk()
	{
		uint bitRate = sampleRate * bitsPerSample * channelCount;
		uint bufferSize;

		bufferSize = bitRate != 0 ? bitRate / 8 / 25 : 256_000 / 8 / 25; // MediaCapture do not put data about bit rate in EncodingProfile.Audio.Bitrate when AudioEncodingQuality.Auto

		if (audioFileStream?.Length > (audioChunkNumber * bufferSize) + startingAudioFileStreamLength)
		{
			byte[] buffer = new byte[bufferSize];
			audioFileStream.Seek(-bufferSize, SeekOrigin.End);
			audioFileStream.Read(buffer);
			audioChunkNumber++;

			return buffer;
		}
		else
		{
			return null;
		}
	}
}

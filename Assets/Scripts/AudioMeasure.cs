using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using System.Collections;

public class AudioMeasure : MonoBehaviour
{
    public string MicDevice;

    private const int SampleWindow = 8192;
    private const int SpectrumWindow = 8192;
    /*private const float RefValue = 1.1f;*/
    private const float Threshold = 0.2f;


    private const float ScaleFactorAmplitude = 1500;
    private const float ScaleFactorSpectrum = 500;

    private const float WaitTimeAmplitude = 0.08f;
    private const float WaitTimeFrequency = 0.1f;


    /*public float RmsValue;*/
    public float Amplitude;
    public float Frequency;

    public float maxAmplitude;
    public float minAmplitude;

    private float[] _samples;
    private float[] _spectrum;
    private int _sampleRate;

    private bool isSoundMeasureStarted;

    private static List<float> historyAmplitudeData = new List<float>();
    private static List<float> historyFrequencyData = new List<float>();

    public Text UI_Frequency;
    public Text UI_Amplitude;

    public AudioSource microphoneAudioSource;
 
    public AudioMixerGroup audioMixerGroup;

    private IEnumerator amplitudeCoroutine;
    private IEnumerator frequencyCoroutine;

    public int lowFrequencyBoundary;
    public int highFrequencyBoundary;

    public int lowerSpectrum;
    public int highSpectrum;

    public Slider LowFrequencySlider;
    public Slider HighFrequencySlider;
    public Text UI_LowFrequencyBound;
    public Text UI_HighFrequencyBound;

    private void Awake()
    {

        microphoneAudioSource = GetComponent<AudioSource>();
        isSoundMeasureStarted = false;
        UI_Frequency.text = " - Hz";
        UI_Amplitude.text = " - dB";

        lowFrequencyBoundary = 0;
        highFrequencyBoundary = 20000;

        _sampleRate = AudioSettings.outputSampleRate;

        lowerSpectrum = (lowFrequencyBoundary * SpectrumWindow * 2) / _sampleRate;
        highSpectrum = (highSpectrum * SpectrumWindow * 2) / _sampleRate;

        //Adds a listener to sliders and invokes a method when the value changes.
        LowFrequencySlider.onValueChanged.AddListener(delegate { LowFrequencySliderChange(); });
        HighFrequencySlider.onValueChanged.AddListener(delegate { HighFrequencySliderChange(); });
    }

    private void Update()
    {
        UI_LowFrequencyBound.text = lowFrequencyBoundary.ToString();
        UI_HighFrequencyBound.text = highFrequencyBoundary.ToString();
    }


    public void StartMeasureMicrophone()
    {
        if (isSoundMeasureStarted) return;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        Debug.Log("Starting");
        MicDevice = Microphone.devices[0];

        Microphone.GetDeviceCaps(MicDevice, out int minFreq, out int maxFreq);
        Debug.Log(MicDevice + " min frequency: " + minFreq + " max frequency: " + maxFreq);

        microphoneAudioSource = GetComponent<AudioSource>();

        ResetAmplitudeDataBuffers();
        ResetFrequencyDataBuffers();

        _samples = new float[SampleWindow];
        _spectrum = new float[SpectrumWindow];
        

        lowerSpectrum = lowFrequencyBoundary * SpectrumWindow * 2 / _sampleRate;
        highSpectrum = highFrequencyBoundary * SpectrumWindow * 2 / _sampleRate;

        microphoneAudioSource.outputAudioMixerGroup = audioMixerGroup;

        microphoneAudioSource.clip = Microphone.Start(MicDevice, true, 10, 16100);
        microphoneAudioSource.loop = true;
        microphoneAudioSource.mute = false;
        while (!(Microphone.GetPosition(null) > 0)) { }
        microphoneAudioSource.Play();
        isSoundMeasureStarted = true;

        amplitudeCoroutine = AmplitudeMeasureCoroutine(WaitTimeAmplitude);
        StartCoroutine(amplitudeCoroutine);

        frequencyCoroutine = FrequencyMeasureCoroutine(WaitTimeFrequency);
        StartCoroutine(frequencyCoroutine);

    }

    public void StopMeasureMicrophone()
    {
        if (!isSoundMeasureStarted) return;
        isSoundMeasureStarted = false;

        StopCoroutine(amplitudeCoroutine);
        StopCoroutine(frequencyCoroutine);

        ResetFrequencyDataBuffers();
        ResetAmplitudeDataBuffers();

        UI_Frequency.text = " - Hz";
        UI_Amplitude.text = " - dB";
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

    }

    private void UpdateFrequency()
    {
        lowerSpectrum = (lowFrequencyBoundary * SpectrumWindow * 2) / _sampleRate;
        highSpectrum = (highFrequencyBoundary * SpectrumWindow * 2) / _sampleRate;

        float maxSpectrum = 0;
        var maxIndex = 0;
        int j;
        Frequency = 0;

        microphoneAudioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

        // find the max in spectrum 
        for (j = lowerSpectrum; j < highSpectrum; j++)
        {
            _spectrum[j] *= ScaleFactorSpectrum;
            
            if (_spectrum[j] > maxSpectrum && _spectrum[j] > Threshold)
            {
                maxSpectrum = _spectrum[j];
                maxIndex = j; // index of max
            }
        }

        float freqComponent = maxIndex;

        // interpolate index using neighbours
        if (maxIndex > 0 && maxIndex < SpectrumWindow - 1)
        {
            var dL = _spectrum[maxIndex - 1] / _spectrum[maxIndex];
            var dR = _spectrum[maxIndex + 1] / _spectrum[maxIndex];
            freqComponent += 0.5f * (dR * dR - dL * dL);
        }
        Frequency = freqComponent * (_sampleRate / 2) / SpectrumWindow;


        UI_Frequency.text = Mathf.RoundToInt(SmoothFrequencyAverage(Frequency)) + "Hz";

    }


    private void UpdateAmplitude()
    {
        int i;
        float sum = 0;
        Amplitude = 0;

        microphoneAudioSource.GetOutputData(_samples, 0); // fill array with samples

        
        // sum of squared samples
        for (i = 0; i < SampleWindow; i++)
        {
            _samples[i] *= ScaleFactorAmplitude; // scaleup the amplitude
            sum += (_samples[i]) * (_samples[i]);
            /*_samples[i] = 0;*/
        }
        

        /*RmsValue = Mathf.Sqrt(sum / SampleWindow); // rms = square root of average
        Amplitude = 20 * Mathf.Log10(RmsValue / RefValue); // calculate the dB value*/
        /*Debug.Log(sum);*/
        Amplitude = 10 * Mathf.Log10(sum / SampleWindow) + 20;

        if (Amplitude < 0) Amplitude = 0; // clamp it to -160dB min
        UI_Amplitude.text = Mathf.RoundToInt(SmoothAmplitudeAverage(Amplitude)) + "dB";
        /*UI_Amplitude.text = Mathf.RoundToInt(Amplitude) + "dB";*/

    }

    public void LowFrequencySliderChange()
    {
        lowFrequencyBoundary = (int)LowFrequencySlider.value;
        UI_LowFrequencyBound.text = lowFrequencyBoundary.ToString();

    }

    public void HighFrequencySliderChange()
    {
        highFrequencyBoundary = (int)HighFrequencySlider.value;
        UI_HighFrequencyBound.text = highFrequencyBoundary.ToString();
    }


    IEnumerator AmplitudeMeasureCoroutine(float waitTime)
    {
        while (isSoundMeasureStarted)
        {
            yield return new WaitForSeconds(waitTime);
            UpdateAmplitude();
        }
    }
    IEnumerator FrequencyMeasureCoroutine(float waitTime)
    {
        while (isSoundMeasureStarted)
        {
            yield return new WaitForSeconds(waitTime);
            UpdateFrequency();
        }
    }

    private void ResetAmplitudeDataBuffers()
    {
        historyAmplitudeData = new List<float>();
    }
    private void ResetFrequencyDataBuffers()
    {
        historyFrequencyData = new List<float>();
    }

    private float SmoothAmplitudeAverage(float currentAmplitude)
    {
        if (historyAmplitudeData.Count == 5)
        {
            historyAmplitudeData.RemoveAt(0);
        }
        historyAmplitudeData.Add(currentAmplitude);
        return historyAmplitudeData.Average();
    }

    private float SmoothFrequencyAverage(float currentAmplitude)
    {

        if (historyFrequencyData.Count == 5)
        {
            historyFrequencyData.RemoveAt(0);
        }
        historyFrequencyData.Add(currentAmplitude);

        return historyFrequencyData.Average();
    }


    void OnDisable()
    {
        if (isSoundMeasureStarted)
        {
            StopCoroutine(amplitudeCoroutine);
            StopCoroutine(frequencyCoroutine);
        }
        
        Microphone.End(MicDevice);
        
    }

    void OnDestroy()
    {
        if (isSoundMeasureStarted)
        {
            StopCoroutine(amplitudeCoroutine);
            StopCoroutine(frequencyCoroutine);
        }
        Microphone.End(MicDevice);
    }


    /* void OnApplicationFocus(bool focus)
     {
         if (focus)
         {
             if (isSoundMeasureStarted)
             {
                 StartMeasureMicrophone();
             }
         }
         if (!focus)
         {
             StopMeasureMicrophone();
         }
     }*/
}

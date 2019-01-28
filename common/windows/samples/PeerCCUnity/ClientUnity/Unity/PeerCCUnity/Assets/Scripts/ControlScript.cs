using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;

#if !UNITY_EDITOR
using Windows.Storage;
using Windows.UI.Core;
using Windows.Foundation;
using Windows.Media.Core;
using System.Linq;
using System.Threading.Tasks;
using PeerConnectionClient.Signalling;
using Windows.ApplicationModel.Core;

using PeerConnectionClient.Signalling;
using PeerConnectionClient.Utilities;

using CodecInfo = PeerConnectionClient.Signalling.Conductor.CodecInfo;
using MediaDevice = PeerConnectionClient.Signalling.Conductor.MediaDevice;
using UseMediaStreamTrack = Org.WebRtc.IMediaStreamTrack;
#endif

public class ControlScript : MonoBehaviour
{
    public static ControlScript Instance { get; private set; }

    public uint LocalTextureWidth = 160;
    public uint LocalTextureHeight = 120;
    public uint RemoteTextureWidth = 640;
    public uint RemoteTextureHeight = 480;
    
    public RawImage LocalVideoImage;
    public RawImage RemoteVideoImage;

    public InputField ServerAddressInputField;
    public Button ConnectButton;
    public Button CallButton;
    public RectTransform PeerContent;
    public GameObject TextItemPreftab;

    private enum Status
    {
        NotConnected,
        Connecting,
        Disconnecting,
        Connected,
        Calling,
        EndingCall,
        InCall
    }

    private enum CommandType
    {
        Empty,
        SetNotConnected,
        SetConnected,
        SetInCall,
        AddRemotePeer,
        RemoveRemotePeer
    }

    private struct Command
    {
        public CommandType type;
#if !UNITY_EDITOR
        public Peer remotePeer;
#endif
    }

    private Status status = Status.NotConnected;
    private List<Command> commandQueue = new List<Command>();
    private int selectedPeerIndex = -1;

    public ControlScript()
    {
    }

    void Awake()
    {
    }

#if !UNITY_EDITOR
    private List<MediaDevice> Cameras;
    private List<MediaDevice> Microphones;
    private List<MediaDevice> AudioPlayoutDevices;


    private MediaDevice _selectedCamera;

    /// <summary>
    /// The selected camera.
    /// </summary>
    private MediaDevice SelectedCamera
    {
        get { return _selectedCamera; }
        set
        {
            _selectedCamera = value;

            if (value == null)
            {
                return;
            }

            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedCameraId"] = _selectedCamera.Id;
            Conductor.Instance.SelectVideoDevice(_selectedCamera);
        }
    }

    private ObservableCollection<Peer> Peers;

    private Peer _selectedPeer;

#endif

    private bool IsConnected = false;

    private bool IsMicrophoneEnabled = true;

    private bool IsCameraEnabled = true;

    private bool IsConnecting = false;

    private bool IsDisconnecting = false;

    private bool IsReadyToDisconnect = false;

    private bool IsReadyToConnect = false;

    private bool IsConnectedToPeer = false;

#if !UNITY_EDITOR
    private UseMediaStreamTrack _peerVideoTrack;
    private UseMediaStreamTrack _selfVideoTrack;
    private UseMediaStreamTrack _peerAudioTrack;
    private UseMediaStreamTrack _selfAudioTrack;
#endif

    public bool bCameraEnabled = true;

    public bool bMicrophoneIsOn = true;

    private String PeerConnectionHealthStats;

#if !UNITY_EDITOR
    private ObservableCollection<IceServer> IceServers;

    private IceServer NewIceServer;


    private ObservableCollection<CodecInfo> AudioCodecs;

    private ObservableCollection<CodecInfo> VideoCodecs;

    public CodecInfo SelectedAudioCodec
    {
        get { return Conductor.Instance.AudioCodec; }
        set
        {
            if (Conductor.Instance.AudioCodec == value)
            {
                return;
            }
            Conductor.Instance.AudioCodec = value;
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedAudioCodecId"] = Conductor.Instance.AudioCodec.PreferredPayloadType;
        }
    }

    public CodecInfo SelectedVideoCodec
    {
        get { return Conductor.Instance.VideoCodec; }
        set
        {
            if (Conductor.Instance.VideoCodec == value)
            {
                return;
            }

            Conductor.Instance.VideoCodec = value;
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedVideoCodecId"] = Conductor.Instance.VideoCodec.PreferredPayloadType;
        }
    }

#endif


#if !UNITY_EDITOR
    public void Initialize(CoreDispatcher uiDispatcher)
    {
        var queue = Org.WebRtc.EventQueueMaker.Bind(uiDispatcher);
        Org.WebRtc.WebRtcLib.Setup(queue);

        var settings = ApplicationData.Current.LocalSettings;

        // Get information of cameras attached to the device
        Cameras = new List<MediaDevice>();
        string savedVideoRecordingDeviceId = null;
        if (settings.Values["SelectedCameraId"] != null)
        {
            savedVideoRecordingDeviceId = (string)settings.Values["SelectedCameraId"];
        }
        // Get information of microphones attached to the device
        Microphones = new List<MediaDevice>();
        string savedAudioRecordingDeviceId = null;
        if (settings.Values["SelectedMicrophoneId"] != null)
        {
            savedAudioRecordingDeviceId = (string)settings.Values["SelectedMicrophoneId"];
        }
        AudioPlayoutDevices = new List<MediaDevice>();
        string savedAudioPlayoutDeviceId = null;
        if (settings.Values["SelectedAudioPlayoutDeviceId"] != null)
        {
            savedAudioPlayoutDeviceId = (string)settings.Values["SelectedAudioPlayoutDeviceId"];
        }

        
        RunOnUiThread(async () =>
        {
            foreach (MediaDevice videoCaptureDevice in await Conductor.GetVideoCaptureDevices())
            {
                if (savedVideoRecordingDeviceId != null && savedVideoRecordingDeviceId == videoCaptureDevice.Id)
                {
                    SelectedCamera = videoCaptureDevice;
                }
                Cameras.Add(videoCaptureDevice);
            }

            if (SelectedCamera == null && Cameras.Count > 0)
            {
                SelectedCamera = Cameras.First();
            }

    
            var opRes = SelectedCamera.GetVideoCaptureCapabilities();
            await opRes.AsTask().ContinueWith(resolutions =>
            {
                var uniqueRes = resolutions.Result.GroupBy(test => test.ResolutionDescription).Select(grp => grp.First()).ToList();
                Conductor.CaptureCapability defaultResolution = null;
                foreach (var resolution in uniqueRes)
                {
                    if (defaultResolution == null)
                    {
                        defaultResolution = resolution;
                    }

                    if ((resolution.Width == 896) && (resolution.Height == 504))
                    {
                        defaultResolution = resolution;
                    }
                }

                Conductor.Instance.VideoCaptureProfile = defaultResolution;

            });

        });


        
        // A Peer is connected to the server event handler
        Conductor.Instance.Signaller.OnPeerConnected += (peerId, peerName) =>
        {
            RunOnUiThread(() =>
            {
                if (Peers == null)
                {
                    Peers = new ObservableCollection<Peer>();
                    Conductor.Instance.Peers = Peers;
                }
                Peers.Add(new Peer { Id = peerId, Name = peerName });
                System.Diagnostics.Debug.WriteLine("Peers.Add: " + peerName + " " + peerId.ToString());
            });
            status = Status.Connected;
            Debug.Log("OnPeerConnected!");
        };

        

        // A Peer is disconnected from the server event handler
        Conductor.Instance.Signaller.OnPeerDisconnected += peerId =>
        {
            RunOnUiThread(() =>
            {
                var peerToRemove = Peers?.FirstOrDefault(p => p.Id == peerId);
                if (peerToRemove != null)
                    Peers.Remove(peerToRemove);
            });
        };
        
        // The user is Signed in to the server event handler
        Conductor.Instance.Signaller.OnSignedIn += () =>
        {
            RunOnUiThread(() =>
            {
                IsConnected = true;
                IsMicrophoneEnabled = true;
                IsCameraEnabled = true;
                IsConnecting = false;
            });
        };
        
        // Failed to connect to the server event handler
        Conductor.Instance.Signaller.OnServerConnectionFailure += () =>
        {
            Debug.Log("Failed to connect to server!");
        };
        
        // The current user is disconnected from the server event handler
        Conductor.Instance.Signaller.OnDisconnected += () =>
        {
            RunOnUiThread(() =>
            {
                IsConnected = false;
                IsMicrophoneEnabled = false;
                IsCameraEnabled = false;
                IsDisconnecting = false;
                Peers?.Clear();
            });
        };
        
        // Event handlers for managing the media streams 

        Conductor.Instance.OnAddRemoteTrack += Conductor_OnAddRemoteTrack;
        Conductor.Instance.OnRemoveRemoteTrack += Conductor_OnRemoveRemoteTrack;
        Conductor.Instance.OnAddLocalTrack += Conductor_OnAddLocalTrack;

        Conductor.Instance.OnConnectionHealthStats += Conductor_OnPeerConnectionHealthStats;

        
        //PlotlyManager.UpdateUploadingStatsState += PlotlyManager_OnUpdatedUploadingStatsState;
        //PlotlyManager.OnError += PlotlyManager_OnError;
        // Connected to a peer event handler
        Conductor.Instance.OnPeerConnectionCreated += () =>
        {
            Debug.Log("OnPeerConnectionCreated");
            IsReadyToConnect = false;
            IsConnectedToPeer = true;
        };
        
        // Connection between the current user and a peer is closed event handler
        Conductor.Instance.OnPeerConnectionClosed += () =>
        {
            RunOnUiThread(() =>
            {
                Debug.Log("OnPeerConnectionClosed");

                IsConnectedToPeer = false;

                if (null != _peerVideoTrack) _peerVideoTrack.Element = null; // Org.WebRtc.MediaElementMaker.Bind(obj);
                if (null != _selfVideoTrack) _selfVideoTrack.Element = null; // Org.WebRtc.MediaElementMaker.Bind(obj);

                _peerVideoTrack = null;
                _selfVideoTrack = null;
                _peerAudioTrack = null;
                _selfAudioTrack = null;

                IsMicrophoneEnabled = true;
                IsCameraEnabled = true;

            });
        };
        
        // Ready to connect to the server event handler
        Conductor.Instance.OnReadyToConnect += () => 
        {
            Debug.Log("OnReadyToConnect");
        };
        
        // Initialize the Ice servers list
        IceServers = new ObservableCollection<IceServer>();
        NewIceServer = new IceServer();

        // Prepare to list supported audio codecs
        AudioCodecs = new ObservableCollection<CodecInfo>();
        var audioCodecList = Conductor.GetAudioCodecs();

        // These are features added to existing codecs, they can't decode/encode real audio data so ignore them
        string[] incompatibleAudioCodecs = new string[] { "CN32000", "CN16000", "CN8000", "red8000", "telephone-event8000" };

        // Prepare to list supported video codecs
        VideoCodecs = new ObservableCollection<CodecInfo>();

        // Order the video codecs so that the stable VP8 is in front.
        var videoCodecList = Conductor.GetVideoCodecs().OrderBy(codec =>
        {
            switch (codec.Name)
            {
                case "VP8": return 1;
                case "VP9": return 2;
                case "H264": return 3;
                default: return 99;
            }
        });


        // Load the supported audio/video information into the Settings controls
        RunOnUiThread(() =>
        {
            foreach (var audioCodec in audioCodecList)
            {

                if (!incompatibleAudioCodecs.Contains(audioCodec.Name + audioCodec.ClockRate))
                {
                    AudioCodecs.Add(audioCodec);
                }
            }

            if (AudioCodecs.Count > 0)
            {
                if (settings.Values["SelectedAudioCodecId"] != null)
                {
                    byte id = Convert.ToByte(settings.Values["SelectedAudioCodecId"]);
                    foreach (var audioCodec in AudioCodecs)
                    {
                        var audioCodecId = audioCodec.PreferredPayloadType;
                        if (audioCodecId == id)
                        {
                            SelectedAudioCodec = audioCodec;
                            break;
                        }
                    }
                }
                if (SelectedAudioCodec == null)
                {
                    SelectedAudioCodec = AudioCodecs.First();
                }
            }

            foreach (var videoCodec in videoCodecList)
            {
                VideoCodecs.Add(videoCodec);
            }

            if (VideoCodecs.Count > 0)
            {
                if (settings.Values["SelectedVideoCodecId"] != null)
                {
                    int id = Convert.ToInt32(settings.Values["SelectedVideoCodecId"]);
                    foreach (var videoCodec in VideoCodecs)
                    {
                        var videoCodecId = videoCodec.PreferredPayloadType;
                        if (videoCodecId == id)
                        {
                            SelectedVideoCodec = videoCodec;
                            break;
                        }
                    }
                }
                if (SelectedVideoCodec == null)
                {
                    SelectedVideoCodec = VideoCodecs.First();
                }
            }
        });
                        
        LoadSettings();

    }
#endif

    void Start()
    {
        Instance = this;

#if !UNITY_EDITOR
        
         // Display a permission dialog to request access to the microphone and camera
        Conductor.RequestAccessForMediaCapture().AsTask().ContinueWith(antecedent =>
        {
            if (antecedent.Result)
            {
                Initialize(CoreApplication.MainView.CoreWindow.Dispatcher);
            }
            else
            {

                Debug.Log("Failed to obtain access to multimedia devices!");

            }
        });
        
#endif
        ServerAddressInputField.text = "192.168.11.132";


        Invoke("OnConnectClick", 20.0f);

        Invoke("OnCallClick", 40.0f);
    }

    private void OnEnable()
    {
        {
            Plugin.CreateLocalMediaPlayback();
            IntPtr nativeTex = IntPtr.Zero;
            Plugin.GetLocalPrimaryTexture(LocalTextureWidth, LocalTextureHeight, out nativeTex);
            var primaryPlaybackTexture = Texture2D.CreateExternalTexture((int)LocalTextureWidth, (int)LocalTextureHeight, TextureFormat.BGRA32, false, false, nativeTex);
            LocalVideoImage.texture = primaryPlaybackTexture;
        }

        {
            Plugin.CreateRemoteMediaPlayback();
            IntPtr nativeTex = IntPtr.Zero;
            Plugin.GetRemotePrimaryTexture(RemoteTextureWidth, RemoteTextureHeight, out nativeTex);
            var primaryPlaybackTexture = Texture2D.CreateExternalTexture((int)RemoteTextureWidth, (int)RemoteTextureHeight, TextureFormat.BGRA32, false, false, nativeTex);
            RemoteVideoImage.texture = primaryPlaybackTexture;
        }
    }

    private void OnDisable()
    {
        LocalVideoImage.texture = null;
        Plugin.ReleaseLocalMediaPlayback();
        RemoteVideoImage.texture = null;
        Plugin.ReleaseRemoteMediaPlayback();
    }

    private void Update()
    {
#if !UNITY_EDITOR
        lock (this)
        {
            switch (status)
            {
                case Status.NotConnected:
                    if (!ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = true;
                    if (!ConnectButton.enabled)
                        ConnectButton.enabled = true;
                    if (CallButton.enabled)
                        CallButton.enabled = false;
                    break;
                case Status.Connecting:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (ConnectButton.enabled)
                        ConnectButton.enabled = false;
                    if (CallButton.enabled)
                        CallButton.enabled = false;
                    break;
                case Status.Disconnecting:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (ConnectButton.enabled)
                        ConnectButton.enabled = false;
                    if (CallButton.enabled)
                        CallButton.enabled = false;
                    break;
                case Status.Connected:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (!ConnectButton.enabled)
                        ConnectButton.enabled = true;
                    if (!CallButton.enabled)
                        CallButton.enabled = true;
                    break;
                case Status.Calling:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (ConnectButton.enabled)
                        ConnectButton.enabled = false;
                    if (CallButton.enabled)
                        CallButton.enabled = false;
                    break;
                case Status.EndingCall:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (ConnectButton.enabled)
                        ConnectButton.enabled = false;
                    if (CallButton.enabled)
                        CallButton.enabled = false;
                    break;
                case Status.InCall:
                    if (ServerAddressInputField.enabled)
                        ServerAddressInputField.enabled = false;
                    if (ConnectButton.enabled)
                        ConnectButton.enabled = false;
                    if (!CallButton.enabled)
                        CallButton.enabled = true;
                    break;
                default:
                    break;
            }

            while (commandQueue.Count != 0)
            {
                Command command = commandQueue.First();
                commandQueue.RemoveAt(0);
                switch (status)
                {
                    case Status.NotConnected:
                        if (command.type == CommandType.SetNotConnected)
                        {
                            ConnectButton.GetComponentInChildren<Text>().text = "Connect";
                            CallButton.GetComponentInChildren<Text>().text = "Call";
                        }
                        break;
                    case Status.Connected:
                        if (command.type == CommandType.SetConnected)
                        {
                            ConnectButton.GetComponentInChildren<Text>().text = "Disconnect";
                            CallButton.GetComponentInChildren<Text>().text = "Call";
                        }
                        break;
                    case Status.InCall:
                        if (command.type == CommandType.SetInCall)
                        {
                            ConnectButton.GetComponentInChildren<Text>().text = "Disconnect";
                            CallButton.GetComponentInChildren<Text>().text = "Hang Up";
                        }
                        break;
                    default:
                        break;
                }
                if (command.type == CommandType.AddRemotePeer)
                {
                    GameObject textItem = (GameObject)Instantiate(TextItemPreftab);
                    textItem.transform.SetParent(PeerContent);
                    textItem.GetComponent<Text>().text = command.remotePeer.Name;
                    EventTrigger trigger = textItem.GetComponentInChildren<EventTrigger>();
                    EventTrigger.Entry entry = new EventTrigger.Entry();
                    entry.eventID = EventTriggerType.PointerDown;
                    entry.callback.AddListener((data) => { OnRemotePeerItemClick((PointerEventData)data); });
                    trigger.triggers.Add(entry);
                    if (selectedPeerIndex == -1)
                    {
                        textItem.GetComponent<Text>().fontStyle = FontStyle.Bold;
                        selectedPeerIndex = PeerContent.transform.childCount - 1;
                    }
                }
                else if (command.type == CommandType.RemoveRemotePeer)
                {
                    for (int i = 0; i < PeerContent.transform.childCount; i++)
                    {
                        if (PeerContent.GetChild(i).GetComponent<Text>().text == command.remotePeer.Name)
                        {
                            PeerContent.GetChild(i).SetParent(null);
                            if (selectedPeerIndex == i)
                            {
                                if (PeerContent.transform.childCount > 0)
                                {
                                    PeerContent.GetChild(0).GetComponent<Text>().fontStyle = FontStyle.Bold;
                                    selectedPeerIndex = 0;
                                }
                                else
                                {
                                    selectedPeerIndex = -1;
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }
#endif
    }

    public void OnConnectClick()
    {
        Debug.Log("ConnectClick!");
#if !UNITY_EDITOR
        lock (this)
        {
            if (status == Status.NotConnected)
            {
                new Task(() =>
                {
                    Conductor.Instance.StartLogin(ServerAddressInputField.text, "8888");
                }).Start();
                status = Status.Connecting;
            }
            else if (status == Status.Connected)
            {
                new Task(() =>
                {
                    var task = Conductor.Instance.DisconnectFromServer();
                }).Start();

                status = Status.Disconnecting;
                selectedPeerIndex = -1;
                PeerContent.DetachChildren();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("OnConnectClick() - wrong status - " + status);
            }
        }
#endif
    }

    public void OnCallClick()
    {
        Debug.Log("CallClick!");
        selectedPeerIndex = 0;
#if !UNITY_EDITOR
        lock (this)
        {
            if (status == Status.Connected)
            {
                if (selectedPeerIndex == -1)
                    return;
                new Task(() =>
                {
                    Peer conductorPeer = Conductor.Instance.Peers[selectedPeerIndex];
                    if (conductorPeer != null)
                    {
                        Conductor.Instance.ConnectToPeer(conductorPeer);
                    }
                }).Start();
                status = Status.Calling;
            }
            else if (status == Status.InCall)
            {
                new Task(() =>
                {
                    var task = Conductor.Instance.DisconnectFromPeer();
                }).Start();
                status = Status.EndingCall;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("OnCallClick() - wrong status - " + status);
            }
        }
#endif
    }

    public void OnRemotePeerItemClick(PointerEventData data)
    {
#if !UNITY_EDITOR
        for (int i = 0; i < PeerContent.transform.childCount; i++)
        {
            if (PeerContent.GetChild(i) == data.selectedObject.transform)
            {
                data.selectedObject.GetComponent<Text>().fontStyle = FontStyle.Bold;
                selectedPeerIndex = i;
            }
            else
            {
                PeerContent.GetChild(i).GetComponent<Text>().fontStyle = FontStyle.Normal;
            }
        }
#endif
    }

#if !UNITY_EDITOR
    //call while OnSuspending
    public async Task OnAppSuspending()
    {
        Conductor.Instance.CancelConnectingToPeer();

        if (IsConnectedToPeer)
        {
            await Conductor.Instance.DisconnectFromPeer();
        }
        if (IsConnected)
        {
            IsDisconnecting = true;
            await Conductor.Instance.DisconnectFromServer();
        }
    }

    private void RunOnUiThread(Action fn)
    {
        var asyncOp = CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, new DispatchedHandler(fn));
    }


    private void Conductor_OnAddRemoteTrack(UseMediaStreamTrack track)
    {
        if (track.Kind == "video")
        {
            _peerVideoTrack = track;

            if (_peerVideoTrack != null)
            {
                var source = track.Source.Source;
                Plugin.LoadRemoteMediaStreamSource((MediaStreamSource)source);
            }
        }
        else if (track.Kind == "audio")
        {
            _peerAudioTrack = track;
        }

        IsReadyToDisconnect = true;
    }

    private void Conductor_OnRemoveRemoteTrack(UseMediaStreamTrack track)
    {
        RunOnUiThread(() =>
        {
            if (track.Kind == "video")
            {
                _peerVideoTrack.Element = null; //Org.WebRtc.MediaElementMaker.Bind(obj)
            }
        });
    }

    private void Conductor_OnAddLocalTrack(UseMediaStreamTrack track)
    {

        if (track.Kind == "video")
        {
            _selfVideoTrack = track;
            if (_selfVideoTrack != null)
            {
                var source = track.Source.Source;
                Plugin.LoadLocalMediaStreamSource((MediaStreamSource)source);


                RunOnUiThread(() =>
                {
                    if (bCameraEnabled)
                    {
                        _selfVideoTrack.Enabled = true;
                    }
                    else
                    {
                        _selfVideoTrack.Enabled = false;
                    }
                });

            }
        }
        if (track.Kind == "audio")
        {
            _selfAudioTrack = track;
            if (_selfAudioTrack != null)
            {
                RunOnUiThread(() =>
                {
                    if (bMicrophoneIsOn)
                    {
                        _selfAudioTrack.Enabled = true;
                    }
                    else
                    {
                        _selfAudioTrack.Enabled = false;
                    }
                });
            }
        }
    }
    private void Conductor_OnPeerConnectionHealthStats(String stats)
    {
        PeerConnectionHealthStats = stats;
    }

    void LoadSettings()
    {
        var settings = ApplicationData.Current.LocalSettings;

        // Default values:
        var configTraceServerIp = "192.168.11.132";
        var configTraceServerPort = "55000";
        var peerCcServerIp = new ValidableNonEmptyString("192.168.11.132");
        var ntpServerAddress = new ValidableNonEmptyString("time.windows.com");
        var peerCcPortInt = 8888;

        if (settings.Values["PeerCCServerIp"] != null)
        {
            peerCcServerIp = new ValidableNonEmptyString((string)settings.Values["PeerCCServerIp"]);
        }

        if (settings.Values["PeerCCServerPort"] != null)
        {
            peerCcPortInt = Convert.ToInt32(settings.Values["PeerCCServerPort"]);
        }

        var configIceServers = new ObservableCollection<IceServer>();

        if (settings.Values["TraceServerIp"] != null)
        {
            configTraceServerIp = (string)settings.Values["TraceServerIp"];
        }

        if (settings.Values["TraceServerPort"] != null)
        {
            configTraceServerPort = (string)settings.Values["TraceServerPort"];
        }

        bool useDefaultIceServers = true;
        if (settings.Values["IceServerList"] != null)
        {
            try
            {
                configIceServers = XmlSerializer<ObservableCollection<IceServer>>.FromXml((string)settings.Values["IceServerList"]);
                useDefaultIceServers = false;
            }
            catch (Exception ex)
            {
                Debug.Log("[Error] Failed to load IceServer from config, using defaults (ex=" + ex.Message + ")");
            }
        }
        if (useDefaultIceServers)
        {
            // Default values:
            configIceServers.Clear();
            configIceServers.Add(new IceServer("stun.l.google.com:19302", IceServer.ServerType.STUN));
            configIceServers.Add(new IceServer("stun1.l.google.com:19302", IceServer.ServerType.STUN));
            configIceServers.Add(new IceServer("stun2.l.google.com:19302", IceServer.ServerType.STUN));
            configIceServers.Add(new IceServer("stun3.l.google.com:19302", IceServer.ServerType.STUN));
            configIceServers.Add(new IceServer("stun4.l.google.com:19302", IceServer.ServerType.STUN));
        }

        if (settings.Values["NTPServer"] != null && (string)settings.Values["NTPServer"] != "")
        {
            ntpServerAddress = new ValidableNonEmptyString((string)settings.Values["NTPServer"]);
        }

        RunOnUiThread(() =>
        {
            IceServers = configIceServers;
            Debug.Log("TraceServerIp = " + configTraceServerIp.ToString());
            Debug.Log("TraceServerPort = " + configTraceServerPort.ToString());
            Debug.Log("Ip = " + peerCcServerIp.ToString());
            Debug.Log("NtpServer = " + ntpServerAddress.ToString());
            var Port = new ValidableIntegerString(peerCcPortInt, 0, 65535);
            Debug.Log("Port = " + Port.ToString());
        });

        Conductor.Instance.ConfigureIceServers(configIceServers);
    }
#endif
    private static class Plugin
    {
        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "CreateLocalMediaPlayback")]
        internal static extern void CreateLocalMediaPlayback();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "CreateRemoteMediaPlayback")]
        internal static extern void CreateRemoteMediaPlayback();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "ReleaseLocalMediaPlayback")]
        internal static extern void ReleaseLocalMediaPlayback();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "ReleaseRemoteMediaPlayback")]
        internal static extern void ReleaseRemoteMediaPlayback();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "GetLocalPrimaryTexture")]
        internal static extern void GetLocalPrimaryTexture(UInt32 width, UInt32 height, out System.IntPtr playbackTexture);

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "GetRemotePrimaryTexture")]
        internal static extern void GetRemotePrimaryTexture(UInt32 width, UInt32 height, out System.IntPtr playbackTexture);

#if !UNITY_EDITOR
        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "LoadLocalMediaStreamSource")]
        internal static extern void LoadLocalMediaStreamSource(MediaStreamSource IMediaSourceHandler);

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "UnloadLocalMediaStreamSource")]
        internal static extern void UnloadLocalMediaStreamSource();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "LoadRemoteMediaStreamSource")]
        internal static extern void LoadRemoteMediaStreamSource(MediaStreamSource IMediaSourceHandler);

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "UnloadRemoteMediaStreamSource")]
        internal static extern void UnloadRemoteMediaStreamSource();
#endif

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "LocalPlay")]
        internal static extern void LocalPlay();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "RemotePlay")]
        internal static extern void RemotePlay();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "LocalPause")]
        internal static extern void LocalPause();

        [DllImport("MediaEngineUWP", CallingConvention = CallingConvention.StdCall, EntryPoint = "RemotePause")]
        internal static extern void RemotePause();
    }
}

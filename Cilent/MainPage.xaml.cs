using Plugin.NFC;
using System.Text;
using System.Text.Json;

namespace MaiMaiPortableNFC
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        public const string ALERT_TITLE = "NFC";
        public const string MIME_TYPE = "application/com.companyname.maimaiportablenfc";
        public string ServerIP = "";
        public string ServerPort = "8088";

        NFCNdefTypeFormat _type;
        bool _makeReadOnly = false;
        bool _eventsAlreadySubscribed = false;
        bool _isDeviceiOS = false;

        /// <summary>
        /// Property that tracks whether the Android device is still listening,
        /// so it can indicate that to the user.
        /// </summary>
        public bool DeviceIsListening
        {
            get => _deviceIsListening;
            set
            {
                _deviceIsListening = value;
                OnPropertyChanged(nameof(DeviceIsListening));
            }
        }
        private bool _deviceIsListening;

        private bool _nfcIsEnabled;
        public bool NfcIsEnabled
        {
            get => _nfcIsEnabled;
            set
            {
                _nfcIsEnabled = value;
                OnPropertyChanged(nameof(NfcIsEnabled));
                OnPropertyChanged(nameof(NfcIsDisabled));
            }
        }

        public bool NfcIsDisabled => !NfcIsEnabled;

        private bool _connectedToServer = false;

        public bool ConnectedToServer
        {
            get => _connectedToServer;
            set
            {
                _connectedToServer = value;
                OnPropertyChanged(nameof(ConnectedToServer));
                OnPropertyChanged(nameof(NotConnectedToServer));
            }
        }

        public bool NotConnectedToServer => !ConnectedToServer;


        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // In order to support Mifare Classic 1K tags (read/write), you must set legacy mode to true.
            CrossNFC.Legacy = true;

            if (CrossNFC.IsSupported)
            {
                if (!CrossNFC.Current.IsAvailable)
                    await ShowAlert("NFC is not available");

                NfcIsEnabled = CrossNFC.Current.IsEnabled;
                if (!NfcIsEnabled)
                    await ShowAlert("NFC is disabled");

                if (DeviceInfo.Platform == DevicePlatform.iOS)
                    _isDeviceiOS = true;

                //// Custom NFC configuration (ex. UI messages in French)
                //CrossNFC.Current.SetConfiguration(new NfcConfiguration
                //{
                //	DefaultLanguageCode = "fr",
                //	Messages = new UserDefinedMessages
                //	{
                //		NFCSessionInvalidated = "Session invalidée",
                //		NFCSessionInvalidatedButton = "OK",
                //		NFCWritingNotSupported = "L'écriture des TAGs NFC n'est pas supporté sur cet appareil",
                //		NFCDialogAlertMessage = "Approchez votre appareil du tag NFC",
                //		NFCErrorRead = "Erreur de lecture. Veuillez rééssayer",
                //		NFCErrorEmptyTag = "Ce tag est vide",
                //		NFCErrorReadOnlyTag = "Ce tag n'est pas accessible en écriture",
                //		NFCErrorCapacityTag = "La capacité de ce TAG est trop basse",
                //		NFCErrorMissingTag = "Aucun tag trouvé",
                //		NFCErrorMissingTagInfo = "Aucune information à écrire sur le tag",
                //		NFCErrorNotSupportedTag = "Ce tag n'est pas supporté",
                //		NFCErrorNotCompliantTag = "Ce tag n'est pas compatible NDEF",
                //		NFCErrorWrite = "Aucune information à écrire sur le tag",
                //		NFCSuccessRead = "Lecture réussie",
                //		NFCSuccessWrite = "Ecriture réussie",
                //		NFCSuccessClear = "Effaçage réussi"
                //	}
                //});

                await AutoStartAsync().ConfigureAwait(false);
            }
        }

        protected override bool OnBackButtonPressed()
        {
            Task.Run(() => StopListening());
            return base.OnBackButtonPressed();
        }

        /// <summary>
        /// Auto Start Listening
        /// </summary>
        /// <returns></returns>
        async Task AutoStartAsync()
        {
            // Some delay to prevent Java.Lang.IllegalStateException "Foreground dispatch can only be enabled when your activity is resumed" on Android
            await Task.Delay(500);
            await StartListeningIfNotiOS();
        }

        /// <summary>
        /// Subscribe to the NFC events
        /// </summary>
        void SubscribeEvents()
        {
            if (_eventsAlreadySubscribed)
                UnsubscribeEvents();

            _eventsAlreadySubscribed = true;

            CrossNFC.Current.OnMessageReceived += Current_OnMessageReceived;
            CrossNFC.Current.OnNfcStatusChanged += Current_OnNfcStatusChanged;
            CrossNFC.Current.OnTagListeningStatusChanged += Current_OnTagListeningStatusChanged;

            if (_isDeviceiOS)
                CrossNFC.Current.OniOSReadingSessionCancelled += Current_OniOSReadingSessionCancelled;
        }

        /// <summary>
        /// Unsubscribe from the NFC events
        /// </summary>
        void UnsubscribeEvents()
        {
            CrossNFC.Current.OnMessageReceived -= Current_OnMessageReceived;
            CrossNFC.Current.OnNfcStatusChanged -= Current_OnNfcStatusChanged;
            CrossNFC.Current.OnTagListeningStatusChanged -= Current_OnTagListeningStatusChanged;

            if (_isDeviceiOS)
                CrossNFC.Current.OniOSReadingSessionCancelled -= Current_OniOSReadingSessionCancelled;

            _eventsAlreadySubscribed = false;
        }

        /// <summary>
        /// Event raised when Listener Status has changed
        /// </summary>
        /// <param name="isListening"></param>
        void Current_OnTagListeningStatusChanged(bool isListening) => DeviceIsListening = isListening;

        /// <summary>
        /// Event raised when NFC Status has changed
        /// </summary>
        /// <param name="isEnabled">NFC status</param>
        async void Current_OnNfcStatusChanged(bool isEnabled)
        {
            NfcIsEnabled = isEnabled;
            await ShowAlert($"NFC has been {(isEnabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Event raised when a NDEF message is received
        /// </summary>
        /// <param name="tagInfo">Received <see cref="ITagInfo"/></param>
        async void Current_OnMessageReceived(ITagInfo tagInfo)
        {
            if (tagInfo == null)
            {
                await ShowAlert("No tag found");
                return;
            }

            // Customized serial number
            var identifier = tagInfo.Identifier;
            var serialNumber = NFCUtils.ByteArrayToHexString(identifier, ":");
            var title = !string.IsNullOrWhiteSpace(serialNumber) ? $"Tag [{serialNumber}]" : "Tag Info";

            if (ConnectedToServer)
            {
                using (HttpClient client = new())
                {
                    try
                    {
                        HttpResponseMessage response = await client.GetAsync($"http://{ServerIP}:{ServerPort}/use?serialNumber={serialNumber}");
                        string responseBody = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            await ShowAlert(SimpleJsonReader(responseBody), ServerIP);
                            return;
                        }

                        if (responseBody.Contains("Card not found"))
                        {
                            // Create Card
                            bool isCorrect = false;
                            string accessCode = "";
                            while (!isCorrect)
                            {
                                accessCode = await ShowInputAlert("Enter the access code", "Bind a new card");
                                if (string.IsNullOrEmpty(accessCode))
                                {
                                    await ShowAlert("Access code cannot be empty");
                                    continue;
                                }
                                // Ask for confirmation
                                isCorrect = await DisplayAlert("Confirmation", $"Are you sure you want to bind this card with access code \n{accessCode}?", "Yes", "No");
                            }


                            // Send the access code to the server
                            response = await client.GetAsync($"http://{ServerIP}:{ServerPort}/add?serialNumber={serialNumber}&accessCode={accessCode}");
                            if(response.IsSuccessStatusCode)
                            {
                                responseBody = await response.Content.ReadAsStringAsync();
                                await ShowAlert(SimpleJsonReader(responseBody), ServerIP);
                            }
                            else
                            {
                                throw new Exception("Failed to bind the card");
                            }
                        }
                        else
                        {
                            throw new Exception(responseBody);
                        }
                    }
                    catch (Exception ex)
                    {
                        await ShowAlert(ex.Message);
                        ConnectedToServer = false;
                    }
                }
            }
            else
                await ShowAlert($"SerialNumber: {tagInfo.SerialNumber}", title);
        }

        /// <summary>
        /// Start listening for NFC Tags when "READ TAG" button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        async void Button_Clicked_StartListening(object sender, System.EventArgs e) => await BeginListening();

        /// <summary>
        /// Stop listening for NFC tags
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        async void Button_Clicked_StopListening(object sender, System.EventArgs e) => await StopListening();

        /// <summary>
        /// Event raised when user cancelled NFC session on iOS 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Current_OniOSReadingSessionCancelled(object sender, EventArgs e) => Debug("iOS NFC Session has been cancelled");

        /// <summary>
        /// Write a debug message in the debug console
        /// </summary>
        /// <param name="message">The message to be displayed</param>
        void Debug(string message) => System.Diagnostics.Debug.WriteLine(message);

        /// <summary>
        /// Display an alert
        /// </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Alert title</param>
        /// <returns>The task to be performed</returns>
        Task ShowAlert(string message, string title = null) => DisplayAlert(string.IsNullOrWhiteSpace(title) ? ALERT_TITLE : title, message, "OK");

        async Task<string> ShowInputAlert(string message, string title = null)
        {
            return await DisplayPromptAsync(string.IsNullOrWhiteSpace(title) ? ALERT_TITLE : title, message, "OK", "Cancel", "");
        }

        /// <summary>
        /// Task to start listening for NFC tags if the user's device platform is not iOS
        /// </summary>
        /// <returns>The task to be performed</returns>
        async Task StartListeningIfNotiOS()
        {
            if (_isDeviceiOS)
            {
                SubscribeEvents();
                return;
            }
            await BeginListening();
        }

        /// <summary>
        /// Task to safely start listening for NFC Tags
        /// </summary>
        /// <returns>The task to be performed</returns>
        async Task BeginListening()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SubscribeEvents();
                    CrossNFC.Current.StartListening();
                    InfoLabel.Text = "Listening resumed";
                });
            }
            catch (Exception ex)
            {
                await ShowAlert(ex.Message);
            }
        }

        /// <summary>
        /// Task to safely stop listening for NFC tags
        /// </summary>
        /// <returns>The task to be performed</returns>
        async Task StopListening()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CrossNFC.Current.StopListening();
                    UnsubscribeEvents();
                    InfoLabel.Text = "Listening has been stopped";
                });
            }
            catch (Exception ex)
            {
                await ShowAlert(ex.Message);
            }
        }

        /// <summary>
        /// Try to connect to the server
        /// </summary>
        /// <returns></returns>
        private async void Button_Clicked(object sender, EventArgs e)
        {
            if (ConnectedToServer)
            {
                await ShowAlert("You're connecting to a new server");
            }
            using (HttpClient client = new())
            {
                if (string.IsNullOrEmpty(ServerIP))
                {
                    await ShowAlert("Please enter the server IP address");
                    return;
                }
                try
                {
                    HttpResponseMessage response = await client.GetAsync($"http://{ServerIP}:{ServerPort}/");
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        await ShowAlert(SimpleJsonReader(responseBody), ServerIP);
                        ConnectedToServer = true;
                    }
                    else
                    {
                        await ShowAlert("Failed to connect to the server");
                        ConnectedToServer = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug(ex.ToString());
                    await ShowAlert(ex.ToString(), $"http://{ServerIP}:{ServerPort}");
                    ConnectedToServer = false;
                }
            }
        }

        /// <summary>
        /// Reads the JSON string
        /// </summary>
        /// <returns></returns>
        private string SimpleJsonReader(string json)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;
                var message = root.GetProperty("message").GetString();
                if (message is null)
                {
                    var error = root.GetProperty("error").GetString();
                    return error is null ? "No message or error found" : error;
                }
                return message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private void Entry_TextChanged_IP(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                ServerIP = entry.Text;
                ConnectedToServer = false;
            }
        }

        private void Entry_TextChanged_Port(object sender, TextChangedEventArgs e)
        {
            if (sender is Entry entry)
            {
                ServerPort = entry.Text;
                ConnectedToServer = false;
            }
        }
    }

}

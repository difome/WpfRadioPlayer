using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Un4seen.Bass;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private int streamHandle;
        private bool isPlaying = false;
        private bool isUpdatingMetadata = false;
        private bool isLoading = false;



        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            radioStationList.DataContext = viewModel;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (!Bass.BASS_Init(-1, 48000, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero))
            {
                MessageBox.Show("Error initializing BASS");
                Close();
            }


        }

        private async Task PlayButton_ClickAsync()
        {
            metadataLabel.Content = "Подключение...";
            isUpdatingMetadata = false;

            var selectedRadioStation = radioStationList.SelectedItem as RadioStationJson;
            if (selectedRadioStation == null)
            {
                metadataLabel.Content = "Выберите радиостанцию";
                return;
            }

            radioNameLabel.Content = selectedRadioStation.Name;

            if (isLoading)
            {
                metadataLabel.Content = "Поток уже загружается...";
                return;
            }

            if (isPlaying)
            {
                Bass.BASS_ChannelStop(streamHandle);
                isPlaying = false;
                isUpdatingMetadata = false;

            }

            metadataLabel.Content = "Подключение...";
            isUpdatingMetadata = false;

            isLoading = true;
            var streamCreationTask = Task.Run(() => Bass.BASS_StreamCreateURL(selectedRadioStation.Url, 0, BASSFlag.BASS_STREAM_STATUS, null, IntPtr.Zero));

            streamHandle = await streamCreationTask;
            Bass.BASS_ChannelSetAttribute(streamHandle, BASSAttribute.BASS_ATTRIB_VOL, 0.5f);

            isPlaying = true;


            isLoading = false;
            isUpdatingMetadata = true;

            if (!Bass.BASS_ChannelPlay(streamHandle, false))
            {
                metadataLabel.Content = "Ошибка при воспроизведение потока";
                return;
            }
            else
            {
                isPlaying = true;
                playButton.Content = "Пауза";
                isUpdatingMetadata = true;

                await UpdateMetadataAsync();
            }
        }
       

        private void metadataLabel_DoubleClick(object sender, EventArgs e)
        {
            Clipboard.SetDataObject(metadataLabel.Content);
        }


        private async Task UpdateMetadataAsync()
        {
            while (isPlaying)
            {


                var selectedRadioStation = radioStationList.SelectedItem as RadioStationJson;

                radioNameLabel.Content = selectedRadioStation.Name;
                var tagsHandle = Bass.BASS_ChannelGetTags(streamHandle, BASSTag.BASS_TAG_META);

                if (tagsHandle != IntPtr.Zero)
                {
                    var tags = Utils.IntPtrAsStringAnsi(tagsHandle);

                    var streamTitleMatch = Regex.Match(tags, @"StreamTitle='(?<title>.+?)';");


                    if (streamTitleMatch.Success)
                    {
                        var streamTitle = streamTitleMatch.Groups["title"].Value;


           



                        /**
                        Encoding iso8859 = Encoding.GetEncoding("iso-8859-1");
                        Encoding windows1251 = Encoding.GetEncoding("windows-1251");
                        byte[] isoBytes = iso8859.GetBytes(streamTitleInput);
                        byte[] winBytes = Encoding.Convert(iso8859, windows1251, isoBytes);
                        string output = windows1251.GetString(winBytes);

                        metadataLabel.Content = output.Trim();
                        **/

                        if (streamTitle != null)
                        {
                            metadataLabel.Content = "Какая то непонятная хуета играет";
                        }

                        metadataLabel.Content = streamTitle.Trim();



                    }
                    else
                        {
                        metadataLabel.Content = selectedRadioStation.Name;
                    }





                }

                /*
                if (tags != null)
                {
                    string ConTitle = String.Join("", tags);
                    string FullTitle = Regex.Match(ConTitle,
                      "(StreamTitle=')(.*)(';StreamUrl)").Groups[2].Value.Trim();
                    string[] Title = Regex.Split(FullTitle, " - ");
                    string Titledata = Regex.Match(ConTitle, "(StreamTitle=')(.*)(\\(.*\\)';StreamUrl)").Groups[2].Value.Trim();

                    metadataLabel.Content = "Играет: " + ConTitle;
                } else {
                    metadataLabel.Content = "Играет: " + selectedRadioStation.Name;
                }
                */




                await Task.Delay(5000);
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (playButton.Content == "Пауза")
            {
                Bass.BASS_ChannelStop(streamHandle);
                isPlaying = false;
                isUpdatingMetadata = false;

                playButton.Content = "Продолжить";

            }
            else
            {
                await PlayButton_ClickAsync();

            }
        }

        private async void radioStationList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await PlayButton_ClickAsync();
        }


        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var volume = (float)volumeSlider.Value / 100f;
            Bass.BASS_ChannelSetAttribute(streamHandle, BASSAttribute.BASS_ATTRIB_VOL, volume);
        }



        protected override void OnClosed(EventArgs e)
        {
            // Free the stream and unload the BASS library when the window is closed
            Bass.BASS_StreamFree(streamHandle);
            Bass.BASS_Free();

            base.OnClosed(e);
        }
    }
}

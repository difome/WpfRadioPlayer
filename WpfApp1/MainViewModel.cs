using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
namespace WpfApp1
{
    public class MainViewModel
    {
        public ObservableCollection<RadioStationJson> RadioStations { get; set; }

        public MainViewModel()
        {
            RadioStations = new ObservableCollection<RadioStationJson>();
            LoadRadioStations();
            /*
            RadioStations = new ObservableCollection<RadioStation>
            {
               
                new RadioStation { Name = "Дала FM", Url = "http://178.88.167.62:8080/DALA_320" },
                new RadioStation { Name = "Руки Вверх!", Url = "https://radiorecord.hostingradio.ru/rv96.aacp" },
                new RadioStation { Name = "Веснушка FM", Url = "https://radiorecord.hostingradio.ru/deti96.aacp" },
                new RadioStation { Name = "Медляк FM", Url = "https://radiorecord.hostingradio.ru/mdl96.aacp" },
                new RadioStation { Name = "Нафталин FM", Url = "https://radiorecord.hostingradio.ru/naft96.aacp" },
                new RadioStation { Name = "Europa Plus", Url = "http://ep256.hostingradio.ru:8052/europaplus256.mp3" }
               

        };    */
        }

        private void LoadRadioStations()
        {
            // Загрузить данные из файла JSON
            string json = File.ReadAllText("./stations.json");

            // Десериализовать данные из JSON в список RadioStationJson
            List<RadioStationJson> jsonList = JsonSerializer.Deserialize<List<RadioStationJson>>(json);

            // Конвертировать каждый элемент списка RadioStationJson в экземпляр класса RadioStation и добавить его в коллекцию RadioStations
            foreach (RadioStationJson station in jsonList)
            {
                RadioStations.Add(new RadioStationJson { Id = station.Id, Name = station.Name, Url = station.Url });
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Net.WebSockets;
using Newtonsoft.Json;

namespace VTS_API_Test
{
    internal class Program
    {

        private readonly Uri _vtubeStudioUri = new Uri("ws://127.0.0.1:8001");
        private ClientWebSocket _webSocket;
        private int ws_counter = 0;

        private readonly string VTS_PluginName = "VTubeStudio2VRCFT";
        private readonly string VTS_DeveloperName = "yukkeDevLab";

        static void Main(string[] args)
        {
            var program = new Program();
            program.Run();
        }

        public void Run()
        {
            ConnectAsync().Wait();

            string authToken = RequestAuthTokenAsync().Result;

            var authResult = AuthenticateAsync(authToken).Result;
            if (!authResult)
            {
                Console.WriteLine("Failed to authenticate with VTubeStudio.");
                return;
            }
            Console.WriteLine("Authenticated with VTubeStudio.");

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            // トラッキングデータを受信する
            for (int i = 0; i < 60; i++)
            {
                // 処理中にWebSocketが切断された場合は再接続する
                if (_webSocket.State != WebSocketState.Open)
                {
                    Console.WriteLine("WebSocket is not open. Reconnecting...");
                    ConnectAsync().Wait();
                    authToken = RequestAuthTokenAsync().Result;
                    authResult = AuthenticateAsync(authToken).Result;
                    if (!authResult)
                    {
                        Console.WriteLine("Failed to authenticate with VTubeStudio.");
                        return;
                    }
                    Console.WriteLine("Authenticated with VTubeStudio.");
                }

                // ReceiveTrackingDataAsync().Wait();

                SendBatchAsync(new List<string> {
                    "EyeRightX",
                    "EyeRightY",
                    "EyeLeftX",
                    "EyeLeftY",
                    "EyeOpenRight",
                    "EyeOpenLeft",
                    "BrowRightY",
                    "BrowLeftY",
                    "Brows"
                });

            }

            while (true)
            {
                // 60回以上のリクエストを送信したら終了
                if (ws_counter >= 540)
                {
                    break;
                }
            }

            // 計測時間を出力
            sw.Stop();
            Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds}ms");

            DisconnectAsync().Wait();

        }

        // WSでVTubeStudioに接続する
        // 接続できない場合でも、60秒間リトライする
        public async Task ConnectAsync()
        {
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    await ConnectOnceAsync();
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to connect to VTubeStudio. Retrying in 1 second.");
                    Console.WriteLine(e.Message);
                    await Task.Delay(1000);
                }
            }
        }

        // WSでVTubeStudioに接続する
        public async Task ConnectOnceAsync()
        {
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(_vtubeStudioUri, CancellationToken.None);
            Console.WriteLine("Connected to VTubeStudio.");
        }

        // WSでVTubeStudioとの接続を切断する
        public async Task DisconnectAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            _webSocket.Dispose();
            _webSocket = null;
            Console.WriteLine("Disconnected from VTubeStudio.");
        }

        // WSでVTubeStudioにデータを送信し、返信を待つ
        public async Task<string> SendAndReceiveAsync(string message)
        {
            var sendBuffer = Encoding.UTF8.GetBytes(message);
            var receiveBuffer = new byte[1024];

            await _webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            var receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
            var receiveMessage = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);

            Console.WriteLine("Sent: " + message);
            Console.WriteLine("Received: " + receiveMessage);

            return receiveMessage;
        }

        // VTubeStadio APIの認証トークンをリクエストする 
        public async Task<string> RequestAuthTokenAsync()
        {
            var request = new
            {
                apiName = "VTubeStudioPublicAPI",
                apiVersion = "1.0",
                requestID = "TokenRequestID",
                messageType = "AuthenticationTokenRequest",
                data = new
                {
                    pluginName = VTS_PluginName,
                    pluginDeveloper = VTS_DeveloperName,
                }
            };

            var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            var responseJson = JsonConvert.DeserializeObject<dynamic>(response);

            Console.WriteLine($"Response: {responseJson}");

            if (responseJson.messageType == "AuthenticationTokenResponse")
            {
                return responseJson.data.authenticationToken;
            }
            Console.WriteLine("Failed to get AuthToken.");
            return "";
        }

        // VTuberStudioとの通信が成功した場合はtrueを返す
        public async Task<bool> AuthenticateAsync(string authToken)
        {
            var request = new
            {
                apiName = "VTubeStudioPublicAPI",
                apiVersion = "1.0",
                requestID = "AuthenticationRequestID",
                messageType = "AuthenticationRequest",
                data = new
                {
                    pluginName = VTS_PluginName,
                    pluginDeveloper = VTS_DeveloperName,
                    authenticationToken = authToken
                }
            };

            var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            var responseJson = JsonConvert.DeserializeObject<dynamic>(response);

            if (responseJson.messageType == "AuthenticationResponse" && responseJson.data.authenticated == true)
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        // 複数個所のトラッキングデータを取得する
        // 参考:
        // https://github.com/DenchiSoft/VTubeStudio?tab=readme-ov-file#requesting-list-of-available-tracking-parameters
        public async Task ReceiveTrackingDataAsync()
        {
            var request = new Request
            {
                apiName = "VTubeStudioPublicAPI",
                apiVersion = "1.0",
                requestID = "TrackingDataRequestID",
                messageType = "ParameterValueRequest",
                data = new
                {
                    name = ""
                }
            };

            // 目のトラッキングデータ(right.x,right.y,left.x,left.y)、視線の位置を取得する
            request.data = new { name = "EyeRightX" };
            var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            var responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Received TrackingData: " + responseJson.data.value);

            request.data = new { name = "EyeRightY" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Received TrackingData: " + responseJson.data.value);

            request.data = new { name = "EyeLeftX" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Received TrackingData: " + responseJson.data.value);

            request.data = new { name = "EyeLeftY" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Received TrackingData: " + responseJson.data.value);

            // 目のトラッキングデータ、目の開き具合を取得する
            request.data = new { name = "EyeOpenRight" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Eye Open Right: " + responseJson.data.value);

            request.data = new { name = "EyeOpenLeft" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Eye Open Left: " + responseJson.data.value);

            // 眉のデータを取得する

            // 右の眉がどれくらい横にあるかを取得できる
            request.data = new { name = "BrowRightY" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Brow Right Y: " + responseJson.data.value);

            // 左の眉がどれくらい横にあるかを取得できる
            request.data = new { name = "BrowLeftY" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Brow Left Y: " + responseJson.data.value);

            // 眉がどれくらい上にあるかを取得できる
            request.data = new { name = "Brows" };
            response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
            responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            // Console.WriteLine("Brows: " + responseJson.data.value);

            // Console.WriteLine("Received All TrackingData");
            Console.WriteLine("==============");
        }

        // 複数のリクエストを一つの関数で処理する
        public void SendBatchAsync(List<string> data)
        {
            Request request = new Request
            {
                apiName = "VTubeStudioPublicAPI",
                apiVersion = "1.0",
                requestID = "TrackingDataRequestID",
                messageType = "ParameterValueRequest",
                data = new { name = "" }
            };
            // 別々のデータを同時に処理するためにParallel.ForEachを使用
            Parallel.ForEach(data, async d =>
            {
                try
                {
                    request.data = new { name = d };
                    var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(response);
                    Console.WriteLine("Received TrackingData: " + responseJson.data.value);
                    Console.WriteLine("Counter: " + ws_counter);
                    ws_counter++;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            });
            // 10ms待機
            Thread.Sleep(10);
        }

        // トラッキングデータを取得する
        // public async Task TrackingReceivedAsync(var request)
        // {
        //     var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
        //     var responseJson = JsonConvert.DeserializeObject<dynamic>(response);
        //     Console.WriteLine("Received TrackingData: " + responseJson.data.value);
        // }
    }

    // api呼び出し用のクラス
    public class Request
    {
        public string apiName { get; set; }
        public string apiVersion { get; set; }
        public string requestID { get; set; }
        public string messageType { get; set; }
        public dynamic data { get; set; }
    }
}

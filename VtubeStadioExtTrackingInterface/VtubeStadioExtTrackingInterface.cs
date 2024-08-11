using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using VRCFaceTracking;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VtubeStadioExtTrackingInterface
{
    public class VtubeStadioExtTrackingInterface : ExtTrackingModule
    {

        #region Constants
        private readonly string VTS_APINAME = "VTubeStudioPublicAPI";
        private readonly string VTS_APIVERSION = "1.0";
        private readonly string VTS_PluginName = "VTubeStudio2VRCFT";
        private readonly string VTS_DeveloperName = "yukkeDevLab";

        private readonly Uri _vtubeStudioUri = new Uri("ws://127.0.0.1:8001");
        #endregion

        #region Fields
        private ClientWebSocket _webSocket;
        private Request request = new Request();

        private bool isEyeTrackingActive = false;
        private bool isExpressionTrackingActive = false;
        #endregion

        #region Overrides / init, update, teardown

        /// <summary>
        /// サポートしている機能を返す
        /// </summary>
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        /// <summary>
        /// モジュールの初期化処理
        /// </summary>
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            request.apiName = VTS_APINAME;
            request.apiVersion = VTS_APIVERSION;

            // VTSへの接続処理
            if (!APIConnect())
            {
                return (false, false);
            }
            string authToken = RequestAuthToken();
            if (authToken == "")
            {
                return (false, false);
            }
            var authResult = Authenticate(authToken);
            if (!authResult)
            {
                return (false, false);
            }

            request.requestID = "TrackingDataRequestID";
            request.messageType = "ParameterValueRequest";

            isEyeTrackingActive = eyeAvailable;
            isExpressionTrackingActive = expressionAvailable;
            return (eyeAvailable, expressionAvailable);
        }

        /// <summary>
        /// モジュールの終了処理
        /// </summary>
        public override void Teardown()
        {
            Logger.LogInformation("Teardown VtubeStadioExtTrackingInterface.");
            APIDisconnect();
        }

        /// <summary>
        /// モジュールの更新処理
        /// </summary>
        public override void Update()
        {
            try
            {
                // Logger.LogInformation("logging,update isWebSocketOpen : " + _webSocket.State);
                if (isEyeTrackingActive)
                {
                    // 目のトラッキングデータの取得処理
                    ReceiveEyeTrackingData(ref UnifiedTracking.Data.Eye, ref UnifiedTracking.Data.Shapes);
                    // Logger.LogInformation("logging,update isEyeTrackingActive");
                }
                if (isExpressionTrackingActive)
                {
                    // 表情データの取得処理
                    ReceiveExpressionsTrackingData(ref UnifiedTracking.Data.Shapes);
                    // Logger.LogInformation("logging,update isExpressionTrackingActive");
                }
                // Thread.Sleep(10);
                // Thread.Sleep(500);
            }
            catch (Exception e)
            {
                Logger.LogError("Error in Update.");
                Logger.LogError(e.Message);
                Thread.Sleep(60000);
            }
        }

        #endregion

        /// <summary>
        /// VTubeStudioとの接続処理
        /// 5秒間隔で計60秒リトライする
        /// </summary>
        public bool APIConnect()
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    _webSocket = new ClientWebSocket();
                    _webSocket.ConnectAsync(_vtubeStudioUri, CancellationToken.None).Wait();
                    Logger.LogInformation("Connected to VTubeStudio at " + _vtubeStudioUri);
                    return true;
                }
                catch (Exception e)
                {
                    Logger.LogError("Failed to connect to VTubeStudio. Retrying in 5 second.");
                    Logger.LogError(e.Message);
                    Task.Delay(5000).Wait();
                }
            }

            if (_webSocket.State != WebSocketState.Open)
            {
                Logger.LogError("Failed to connect to VTubeStudio.");
                return false;
            }

            Logger.LogError("connect async, unknown error");
            return false;
        }

        /// <summary>
        /// VTubeStudioとの接続を切断する
        /// </summary>
        public void APIDisconnect()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
            }
            _webSocket.Dispose();
        }

        /// <summary>
        /// VTubeStudioにデータを送信し、返信を待つ
        /// </summary>
        /// <param name="message"></param>
        public async Task<string> SendAndReceiveAsync(string message)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                Logger.LogError("WebSocket is not open.");
                return null;
            }

            var sendBuffer = Encoding.UTF8.GetBytes(message);
            var receiveBuffer = new byte[1024];

            await _webSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            var receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
            var receiveMessage = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);

            // Debug
            // Logger.LogInformation("Sent: " + message);
            // Logger.LogInformation("Received: " + receiveMessage);

            return receiveMessage;
        }

        /// <summary>
        /// VTubeStudioにデータを送信し、返信を待つ
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public string SendAndReceiveAwait(string message)
        {
            return SendAndReceiveAsync(message).Result;
        }

        /// <summary>
        /// VTubeStadio APIの認証トークンをリクエストする
        /// </summary>
        /// <returns></returns>
        public string RequestAuthToken()
        {
            request.requestID = "TokenRequestID";
            request.messageType = "AuthenticationTokenRequest";
            request.data = new { pluginName = VTS_PluginName, pluginDeveloper = VTS_DeveloperName };

            // Logger.LogInformation("Requesting AuthToken..." + JsonConvert.SerializeObject(request));

            var response = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            if (response == null)
            {
                Logger.LogError("Failed to get AuthToken.");
                return "";
            }
            var responseJson = JsonConvert.DeserializeObject<dynamic>(response);

            // Logger.LogInformation($"Response: {responseJson}");

            if (responseJson.messageType == "AuthenticationTokenResponse")
            {
                var authToken = responseJson.data.authenticationToken;
                // Logger.LogInformation("Received AuthToken: " + authToken);
                return authToken;
            }
            // Console.WriteLine(responseJson);
            Logger.LogError("Failed to get AuthToken.");
            return "";
        }

        /// <summary>
        /// VTubeStudioとの認証処理
        /// </summary>
        public bool Authenticate(string authToken)
        {
            request.requestID = "AuthenticationRequestID";
            request.messageType = "AuthenticationRequest";
            request.data = new { pluginName = VTS_PluginName, pluginDeveloper = VTS_DeveloperName, authenticationToken = authToken };

            var response = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            if (response == null)
            {
                Logger.LogError("Failed to authenticate with VTubeStudio.");
                return false;
            }
            var responseJson = JsonConvert.DeserializeObject<dynamic>(response);
            if (responseJson.messageType == "AuthenticationResponse" && responseJson.data.authenticated == true)
            {
                Logger.LogInformation("Authenticated with VTubeStudio.");
                return true;
            }
            else
            {
                Logger.LogError("Failed to authenticate with VTubeStudio.");
                return false;
            }
        }

        /// <summary>
        /// VTubeStudioから目のトラッキングデータを受け取る <br />
        /// 
        /// UnifiedExpressionShapeについてはこちらを参照 <br />
        /// https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes#ue-base-shapes
        /// </summary>
        /// <param name="eyeData"></param>
        /// <param name="shapes"></param>
        public void ReceiveEyeTrackingData(ref UnifiedEyeData eyeData, ref UnifiedExpressionShape[] shapes)
        {
            // 受け取るデータは鏡写しになっている様子
            // 問題があれば修正する

            // データの受け取り処理を並列化したら早くなりそう
            // ↑ 並列化したら、データの取得は高速化できるが、挙動がブレブレになる

            // 目のトラッキングデータ(right.x,right.y,left.x,left.y)、視線を取得する
            // もしかしたら向きが違う可能性
            // eyeData.Right.Gaze.X に、右目のY座標が入る可能性？

            request.data = new { name = "EyeRightX" };
            var resEyeRightX = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeRightX = JsonConvert.DeserializeObject<dynamic>(resEyeRightX);
            var eyeRightX = (float)resJsonEyeRightX.data.value;

            request.data = new { name = "EyeRightY" };
            var resEyeRightY = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeRightY = JsonConvert.DeserializeObject<dynamic>(resEyeRightY);
            var eyeRightY = (float)resJsonEyeRightY.data.value;

            request.data = new { name = "EyeLeftX" };
            var resEyeLeftX = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeLeftX = JsonConvert.DeserializeObject<dynamic>(resEyeLeftX);
            var eyeLeftX = (float)resJsonEyeLeftX.data.value;

            request.data = new { name = "EyeLeftY" };
            var resEyeLeftY = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeLeftY = JsonConvert.DeserializeObject<dynamic>(resEyeLeftY);
            var eyeLeftY = (float)resJsonEyeLeftY.data.value;

            // 目のトラッキングデータ、目の開き具合を取得する
            request.data = new { name = "EyeOpenRight" };
            var resEyeOpenRight = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeOpenRight = JsonConvert.DeserializeObject<dynamic>(resEyeOpenRight);
            var eyeOpenRight = (float)resJsonEyeOpenRight.data.value;

            request.data = new { name = "EyeOpenLeft" };
            var resEyeOpenLeft = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonEyeOpenLeft = JsonConvert.DeserializeObject<dynamic>(resEyeOpenLeft);
            var eyeOpenLeft = (float)resJsonEyeOpenLeft.data.value;

            // 眉の開き具合を取得する
            // 0.0 ~ 1.0の値が入る
            // 0.5で真ん中くらい
            // 0で眉が下がっている、1で眉が上がっている
            request.data = new { name = "Brows" };
            var resBrows = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonBrows = JsonConvert.DeserializeObject<dynamic>(resBrows);
            var brows = (float)resJsonBrows.data.value;

            // 値を格納していく
            eyeData.Right.Gaze.x = eyeRightX;
            eyeData.Right.Gaze.y = eyeRightY;
            eyeData.Left.Gaze.x = eyeLeftX;
            eyeData.Left.Gaze.y = eyeLeftY;
            eyeData.Right.Openness = eyeOpenRight;
            eyeData.Left.Openness = eyeOpenLeft;

            // EyeRightX, EyeRightY, EyeLeftX, EyeLeftY, EyeOpenRight, EyeOpenLeft, browsから、眉の位置、目の位置を推定する

            // 眉の位置
            // var browsShape = (float)brows - 0.5f;
            // var browsShape = (float)brows;
            var browsShape = 1.0f - brows;

            // Logger.LogInformation("browsShape : " + browsShape);
            shapes[(int)UnifiedExpressions.BrowPinchRight].Weight = browsShape;
            shapes[(int)UnifiedExpressions.BrowPinchLeft].Weight = browsShape;

            shapes[(int)UnifiedExpressions.BrowLowererRight].Weight = browsShape;
            shapes[(int)UnifiedExpressions.BrowLowererLeft].Weight = browsShape;

            shapes[(int)UnifiedExpressions.BrowInnerUpRight].Weight = browsShape;
            shapes[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = browsShape;

            shapes[(int)UnifiedExpressions.BrowOuterUpRight].Weight = browsShape;
            shapes[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = browsShape;

            // 目の位置

            // 細め具合
            shapes[(int)UnifiedExpressions.EyeSquintLeft].Weight = (float)eyeOpenLeft;
            shapes[(int)UnifiedExpressions.EyeSquintRight].Weight = (float)eyeOpenRight;

            // 開き具合
            shapes[(int)UnifiedExpressions.EyeWideRight].Weight = (float)eyeOpenRight;
            shapes[(int)UnifiedExpressions.EyeWideLeft].Weight = (float)eyeOpenLeft;

        }

        /// <summary>
        /// VTubeStudioから表情データを受け取る
        /// </summary>
        /// <param name="shapes"></param>
        public void ReceiveExpressionsTrackingData(ref UnifiedExpressionShape[] shapes)
        {

            // 口の開き具合を取得する
            // request.data = new { name = "MouthOpen" };
            request.data = new { name = "VoiceVolumePlusMouthOpen" };

            var resMouthOpen = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonMouthOpen = JsonConvert.DeserializeObject<dynamic>(resMouthOpen);
            var mouthOpen = (float)resJsonMouthOpen.data.value;

            // 口の端の上がりを取得する
            // request.data = new { name = "MouthSmile" };
            request.data = new { name = "VoiceFrequencyPlusMouthSmile" };
            var resMouthSmile = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonMouthSmile = JsonConvert.DeserializeObject<dynamic>(resMouthSmile);
            var mouthSmile = (float)resJsonMouthSmile.data.value;

            // 口を開く
            // アゴの下がり具合を設定
            shapes[(int)UnifiedExpressions.JawOpen].Weight = mouthOpen / 2;

            // 笑顔
            // mouthSmile = 0.0 ~ 1.0
            // デフォルトは0.5
            // 0で口角が下がっている、1で口角が上がっている
            shapes[(int)UnifiedExpressions.MouthCornerPullRight].Weight = mouthSmile;
            shapes[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = mouthSmile;
            shapes[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = mouthSmile;
            shapes[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = mouthSmile;
            shapes[(int)UnifiedExpressions.MouthFrownRight].Weight = mouthSmile;
            shapes[(int)UnifiedExpressions.MouthFrownLeft].Weight = mouthSmile;
        }

        /// <summary>
        /// VTubeStudioとのデータの送受信に使用するクラス
        /// </summary>
        public class Request
        {
            public string apiName { get; set; }
            public string apiVersion { get; set; }
            public string requestID { get; set; }
            public string messageType { get; set; }
            public dynamic data { get; set; }
        }

    }

}
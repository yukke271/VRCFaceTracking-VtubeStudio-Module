using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;

using Microsoft.Extensions.Logging;

using VRCFaceTracking;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

using Newtonsoft.Json;
using System.Net.Security;
using System.Xml;

namespace VtubeStadioExtTrackingInterface
{
    public class VtubeStadioExtTrackingInterface : ExtTrackingModule
    {
        private readonly string VTS_APINAME = "VTubeStudioPublicAPI";
        private readonly string VTS_APIVERSION = "1.0";
        private readonly string VTS_PluginName = "VTubeStudio2VRCFT";
        private readonly string VTS_DeveloperName = "yukkeDevLab";

        private readonly Uri _vtubeStudioUri = new Uri("ws://127.0.0.1:8001");
        private ClientWebSocket _webSocket;

        private Request request = new Request();


        private bool isEyeTrackingActive = false;
        private bool isExpressionTrackingActive = false;

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        /// <summary>
        /// モジュールの初期化処理
        /// </summary>
        /// <param name="eyeAvailable"></param>
        /// <param name="expressionAvailable"></param>
        /// <returns></returns>
        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            request.apiName = VTS_APINAME;
            request.apiVersion = VTS_APIVERSION;

            // VTSへの接続処理
            if (!ConnectAsync().Result)
            {
                return (false, false);
            }
            string authToken = RequestAuthTokenAsync().Result;
            if (authToken == "")
            {
                return (false, false);
            }
            var authResult = AuthenticateAsync(authToken).Result;
            if (!authResult)
            {
                return (false, false);
            }

            isEyeTrackingActive = eyeAvailable;
            isExpressionTrackingActive = expressionAvailable;
            return (eyeAvailable, expressionAvailable);
        }

        // モジュールの終了処理
        public override void Teardown()
        {
            Logger.LogInformation("Teardown VtubeStadioExtTrackingInterface.");
            DisconnectAsync();
        }

        // モジュールの更新処理
        public override void Update()
        {
            try
            {
                // Logger.LogInformation("logging,update isWebSocketOpen : " + _webSocket.State);
                if (isEyeTrackingActive)
                {
                    // 目のトラッキングデータの取得処理
                    ReceiveEyeTrackingDataAsync(ref UnifiedTracking.Data.Eye, ref UnifiedTracking.Data.Shapes);
                    // Logger.LogInformation("logging,update isEyeTrackingActive");
                }
                if (isExpressionTrackingActive)
                {
                    // 表情データの取得処理
                    ReceiveExpressionsTrackingDataAsync(ref UnifiedTracking.Data.Shapes);
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

        // WSでVTubeStudioに接続する
        // 接続できない場合でも、60秒間リトライする
        public async Task<bool> ConnectAsync()
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    _webSocket = new ClientWebSocket();
                    await _webSocket.ConnectAsync(_vtubeStudioUri, CancellationToken.None);
                    Logger.LogInformation("Connected to VTubeStudio at " + _vtubeStudioUri);
                    return true;
                }
                catch (Exception e)
                {
                    Logger.LogError("Failed to connect to VTubeStudio. Retrying in 5 second.");
                    Logger.LogError(e.Message);
                    await Task.Delay(5000);
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

        // WSでVTubeStudioとの接続を切断する
        public void DisconnectAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                // await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
            }
            _webSocket.Dispose();
            // Logger.LogInformation("Disconnected from VTubeStudio.");
        }

        // WSでVTubeStudioにデータを送信し、返信を待つ
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

        // SendAndReceiveAsyncを呼び出し、結果を待って返す
        public string SendAndReceiveAwait(string message)
        {
            return SendAndReceiveAsync(message).Result;
        }

        // VTubeStadio APIの認証トークンをリクエストする 
        public async Task<string> RequestAuthTokenAsync()
        {
            request.requestID = "TokenRequestID";
            request.messageType = "AuthenticationTokenRequest";
            request.data = new { pluginName = VTS_PluginName, pluginDeveloper = VTS_DeveloperName };

            // Logger.LogInformation("Requesting AuthToken..." + JsonConvert.SerializeObject(request));

            var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
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

        // VTuberStudioとの通信が成功した場合はtrueを返す
        public async Task<bool> AuthenticateAsync(string authToken)
        {
            request.requestID = "AuthenticationRequestID";
            request.messageType = "AuthenticationRequest";
            request.data = new { pluginName = VTS_PluginName, pluginDeveloper = VTS_DeveloperName, authenticationToken = authToken };

            var response = await SendAndReceiveAsync(JsonConvert.SerializeObject(request));
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

        // 目のトラッキングデータと表情データを取得する
        // UnifiedExpressionShapeについてはこちらを参照
        // https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes#ue-base-shapes
        public void ReceiveEyeTrackingDataAsync(ref UnifiedEyeData eyeData, ref UnifiedExpressionShape[] shapes)
        {
            // 受け取るデータは鏡写しになっている様子
            // 問題があれば修正する

            // データの受け取り処理を並列化したら早くなりそう

            request.requestID = "TrackingDataRequestID";
            request.messageType = "ParameterValueRequest";

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

            // 複数のトラッキングデータを返す関数を呼び出す。
            // float eyeRightX, eyeRightY, eyeLeftX, eyeLeftY, eyeOpenRight, eyeOpenLeft = asyncReceiveEyeTrackingData(request);

            // 眉の開き具合を取得する
            // 0.0 ~ 1.0の値が入る
            // 0.5で真ん中くらい
            // 0で眉が下がっている、1で眉が上がっている
            request.data = new { name = "Brows" };
            var resBrows = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            var resJsonBrows = JsonConvert.DeserializeObject<dynamic>(resBrows);
            var brows = (float)resJsonBrows.data.value;

            // Operator '-' cannot be applied to operands of type 'float' and 'Newtonsoft.Json.Linq.JValue'
            // というエラーが出るので、floatに変換する必要がある
            // var eyeRightXFloat = (float)eyeRightX;
            // var eyeRightYFloat = (float)eyeRightY;
            // var eyeLeftXFloat = (float)eyeLeftX;
            // var eyeLeftYFloat = (float)eyeLeftY;
            // var browsFloat = (float)brows;

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

        // 表情データを取得する
        public void ReceiveExpressionsTrackingDataAsync(ref UnifiedExpressionShape[] shapes)
        {
            request.requestID = "TrackingDataRequestID";
            request.messageType = "ParameterValueRequest";

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

            // // 唇のX座標を取得する
            // // デフォルトは0
            // // +-30の値が入る
            // request.data = new { name = "FaceAngleX" };
            // var resFaceAngleX = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            // var resJsonFaceAngleX = JsonConvert.DeserializeObject<dynamic>(resFaceAngleX);
            // var faceAngleX = (float)resJsonFaceAngleX.data.value;
            // // +-20に調整する
            // if (faceAngleX > 20)
            // {
            //     faceAngleX = 20;
            // }
            // else if (faceAngleX < -20)
            // {
            //     faceAngleX = -20;
            // }
            // if (faceAngleX == 0)
            // {
            //     faceAngleX = 0.00001f;
            // }
            // // faceAngleX = faceAngleX / 20f;

            // // 唇のY座標を取得する
            // // デフォルトは0
            // // +-30の値が入る
            // request.data = new { name = "FaceAngleY" };
            // var resFaceAngleY = SendAndReceiveAwait(JsonConvert.SerializeObject(request));
            // var resJsonFaceAngleY = JsonConvert.DeserializeObject<dynamic>(resFaceAngleY);
            // var faceAngleY = (float)resJsonFaceAngleY.data.value;
            // // +-20に調整する
            // if (faceAngleY > 20)
            // {
            //     faceAngleY = 20;
            // }
            // else if (faceAngleY < -20)
            // {
            //     faceAngleY = -20;
            // }
            // if (faceAngleY == 0)
            // {
            //     faceAngleY = 0.00001f;
            // }
            // // faceAngleY = faceAngleY / 20f;

            //Debug
            // Logger.LogInformation("mouthOpen : " + mouthOpen);
            // Logger.LogInformation("mouthSmile : " + mouthSmile);
            // Logger.LogInformation("faceAngleX : " + faceAngleX);
            // Logger.LogInformation("faceAngleY : " + faceAngleY);

            // 口を開く
            // アゴの下がり具合を設定
            shapes[(int)UnifiedExpressions.JawOpen].Weight = mouthOpen / 2;
            // 上唇の上がり具合を設定
            // shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight = mouthOpen / 2;
            // shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = mouthOpen / 2;
            // 下唇の下がり具合を設定
            // shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight = mouthOpen / 2;
            // shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = mouthOpen / 2;

            // shapes[(int)UnifiedExpressions.JawOpen].Weight
            // shapes[(int)UnifiedExpressions.JawLeft].Weight
            // shapes[(int)UnifiedExpressions.JawRight].Weight
            // shapes[(int)UnifiedExpressions.JawForward].Weight

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

            // // 唇の位置
            // if (faceAngleY > 0)
            // {
            //     // 上を向いている時、唇が上に引っ張られる
            //     // 右を向いている時、左よりも右の方が上に引っ張られる
            //     if (faceAngleX > 0)
            //     {
            //         shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = faceAngleY;
            //         shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = faceAngleY / 1.5f;
            //         shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = faceAngleY / 2f;
            //         shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = faceAngleY / 2.5f;
            //     }
            //     else
            //     {
            //         shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = faceAngleY / 1.5f;
            //         shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = faceAngleY;
            //         shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = faceAngleY / 2.5f;
            //         shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = faceAngleY / 2f;
            //     }
            // }
            // else
            // {
            //     // 下を向いている時、唇が下に引っ張られる
            //     // 右を向いている時、左よりも右の方が下に引っ張られる
            //     if (faceAngleX > 0)
            //     {
            //         shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = faceAngleY;
            //         shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = faceAngleY / 1.5f;
            //         shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = faceAngleY / 2f;
            //         shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = faceAngleY / 2.5f;
            //     }
            //     else
            //     {
            //         shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = faceAngleY / 1.5f;
            //         shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = faceAngleY;
            //         shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = faceAngleY / 2.5f;
            //         shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = faceAngleY / 2f;
            //     }
            // }

            // if (faceAngleX > 0)
            // {
            //     // 唇のひっぱりと、えくぼ
            //     // 唇が右に引っ張られた場合、左にえくぼができる
            //     shapes[(int)UnifiedExpressions.MouthStretchRight].Weight = faceAngleX;
            //     shapes[(int)UnifiedExpressions.MouthDimpleLeft].Weight = faceAngleX;
            // }
            // else
            // {
            //     shapes[(int)UnifiedExpressions.MouthStretchLeft].Weight = faceAngleX;
            //     shapes[(int)UnifiedExpressions.MouthDimpleRight].Weight = faceAngleX;
            // }


        }

        // 目のトラッキングデータを並列で取得する
        // public (float, float, float, float, float, float) asyncReceiveEyeTrackingData(Request request)
        // {
        //     float eyeRightX, eyeRightY, eyeLeftX, eyeLeftY, eyeOpenRight, eyeOpenLeft;

            

        //     return (eyeRightX, eyeRightY, eyeLeftX, eyeLeftY, eyeOpenRight, eyeOpenLeft);
        // }


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

}
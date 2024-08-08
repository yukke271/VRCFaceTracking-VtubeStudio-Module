# authentication.py

import json
import websockets

async def request_authentication_token(websocket, plugin_name, plugin_developer, plugin_icon=None):
    """
    VTubeStudio APIから認証トークンをリクエストします。
    
    :param websocket: WebSocket接続
    :param plugin_name: プラグインの名前
    :param plugin_developer: プラグインの開発者名
    :param plugin_icon: プラグインのアイコン（オプション）
    :return: 認証トークン、またはNone
    """
    request = {
        "apiName": "VTubeStudioPublicAPI",
        "apiVersion": "1.0",
        "requestID": "TokenRequestID",
        "messageType": "AuthenticationTokenRequest",
        "data": {
            "pluginName": plugin_name,
            "pluginDeveloper": plugin_developer,
            "pluginIcon": plugin_icon
        }
    }

    await websocket.send(json.dumps(request))
    response = await websocket.recv()
    json_response = json.loads(response)
    
    if json_response["messageType"] == "AuthenticationTokenResponse":
        return json_response["data"]["authenticationToken"]
    else:
        return None

async def authenticate_plugin(websocket, plugin_name, plugin_developer, authentication_token):
    """
    VTubeStudio APIを使用してプラグインを認証します。
    
    :param websocket: WebSocket接続
    :param plugin_name: プラグインの名前
    :param plugin_developer: プラグインの開発者名
    :param authentication_token: 認証トークン
    :return: 認証結果の真偽値
    """
    request = {
        "apiName": "VTubeStudioPublicAPI",
        "apiVersion": "1.0",
        "requestID": "AuthenticationRequestID",
        "messageType": "AuthenticationRequest",
        "data": {
            "pluginName": plugin_name,
            "pluginDeveloper": plugin_developer,
            "authenticationToken": authentication_token
        }
    }

    await websocket.send(json.dumps(request))
    response = await websocket.recv()
    json_response = json.loads(response)

    if json_response["messageType"] == "AuthenticationResponse":
        return json_response["data"]["authenticated"]
    else:
        return False
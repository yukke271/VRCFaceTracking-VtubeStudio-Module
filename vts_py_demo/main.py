import asyncio
import json
import websockets

import sys
import pprint

import os
import sys

from authentication import request_authentication_token, authenticate_plugin

async def get_hotkeys(websocket, model_id=None, live2d_item_filename=None):
    request = {
        "apiName": "VTubeStudioPublicAPI",
        "apiVersion": "1.0",
        "requestID": "UniqueRequestIDLessThan64Characters",
        "messageType": "HotkeysInCurrentModelRequest",
        "data": {}
    }

    if model_id is not None:
        request["data"]["modelID"] = model_id

    if live2d_item_filename is not None:
        request["data"]["live2DItemFileName"] = live2d_item_filename

    await websocket.send(json.dumps(request))
    response = await websocket.recv()
    print(f"Received: {response}")
    pprint.pprint(response)

async def get_input_parameter_list_request(websocket):
    request = {
        "apiName": "VTubeStudioPublicAPI",
        "apiVersion": "1.0",
        "requestID": "UniqueRequestIDLessThan64Characters",
        "messageType": "InputParameterListRequest",
        "data": {}
    }

    await websocket.send(json.dumps(request))
    response = await websocket.recv()
    print(f"Received: {response}")
    pprint.pprint(response)

async def get_parameter_value_request(websocket):
    request = {
        "apiName": "VTubeStudioPublicAPI",
        "apiVersion": "1.0",
        "requestID": "UniqueRequestIDLessThan64Characters",
        "messageType": "ParameterValueRequest",
        "data": {
            "name": "EyeRightX",
            # "name2": "EyeRightY",
            # "name3": "EyeLeftX",
            # "name4": "EyeLeftY",
        }
    }

    await websocket.send(json.dumps(request))
    response = await websocket.recv()
    print(f"Received: {response}")
    pprint.pprint(response)

async def main():
    uri = "ws://localhost:8001"
    async with websockets.connect(uri) as websocket:
        plugin_name = "My Cool Plugin"
        plugin_developer = "My Name"
        # 認証トークンの取得
        authentication_token = await request_authentication_token(websocket, plugin_name, plugin_developer)

        if authentication_token:
            print(f"Token: {authentication_token}")
            # 認証の実施
            is_authenticated = await authenticate_plugin(websocket, plugin_name, plugin_developer, authentication_token)
            print(f"Authenticated: {is_authenticated}")
            if is_authenticated:
                # InputParameterListRequestを送信
                # await get_input_parameter_list_request(websocket)

                # ParameterValueRequestを送信し、パラメータの値を取得
                await get_parameter_value_request(websocket)
        else:
            print("Token request failed")

# asyncio.runを使用してメイン関数を実行
asyncio.run(main())
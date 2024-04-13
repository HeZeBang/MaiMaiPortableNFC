from http.server import BaseHTTPRequestHandler, HTTPServer
import json, os, socket, time
import pyautogui

cardList = {}

class SimpleHTTPRequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        
        if self.path == '/':
            response = {'message': 'Congratulations! You\'ve successfully connected to maimai Portable NFC Server!'}
        elif self.path[:4] == '/use':
            try:
                # Format:
                # /use?serialNumber=xxx
                paramsDict = dict(x.split('=') for x in self.path[5:].split('&'))
                serialNumber = paramsDict['serialNumber']
                if serialNumber in cardList:
                    open(aimeFilePath, 'w').write(cardList[serialNumber])
                    pyautogui.keyDown('enter')
                    time.sleep(0.2) # Wait for responding
                    pyautogui.keyUp('enter')
                    response = {'message': f'Card data sent to maimai successfully! (Code: {cardList[serialNumber]}, aime.txt: {aimeFilePath})'}
                else:
                    response = {'error': 'Card not found'}
            except Exception as e:
                response = {'error': f'Internal Error - {e}'}
        elif self.path[:4] == '/add':
            try:
                # Format:
                # /add?serialNumber=xxx&accessCode=yyy
                paramsDict = dict(x.split('=') for x in self.path[5:].split('&'))
                serialNumber = paramsDict['serialNumber']
                accessCode = paramsDict['accessCode']
                cardList[serialNumber] = accessCode
                json.dump(cardList, open('.\data.json', 'w'))
                response = {'message': 'Card added successfully!'}
            except Exception as e:
                response = {'error': f'Internal Error - {e}'}
        else:
            response = {'error': 'Not Found'}
        
        self.send_response(200 if 'error' not in response else 500)
        self.send_header('Content-type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps(response).encode())

def run(server_class=HTTPServer, handler_class=SimpleHTTPRequestHandler, port=8088):
    server_address = ('0.0.0.0', port)
    httpd = server_class(server_address, handler_class)
    print(f'Starting server on port {port}...')
    httpd.serve_forever()

if __name__ == '__main__':
    # Initialize data file
    if(not os.path.exists('.\data.json')):
        json.dump({}, open('.\data.json', 'w'))
    
    cardList = json.load(open('data.json', 'r'))
    print(f"Loaded {len(cardList)} record(s) from data.json")

    if(not os.path.exists('config.ini')):
        open('.\config.ini', 'w').write("[aimeFilePath]=./aime.txt\n[defaultPort]=8088")
        print("Created config.ini, please change the settings in this file and restart again!")
        input("Press any ENTER to continue...")
        exit()

    global aimeFilePath, defaultPort
    aimeFilePath = r".\aime.txt"
    defaultPort = 8088
    
    try:
        with open('.\config.ini', 'r') as file:
            for line in file:
                if(line.startswith('[aimeFilePath]=')):
                    aimeFilePath = line.split("=")[1].replace("\n", "").replace("\\", "/")
                elif(line.startswith('[defaultPort]=')):
                    defaultPort = (int)(line.split("=")[1])
    except:
        open('.\config.ini', 'w').write("[aimeFilePath]=./aime.txt\n[defaultPort]=8088")
        print("Internal Error, resetting config.ini")
        input("Press any ENTER to continue...")
        exit()

    print(f"Using aime.txt file path: {aimeFilePath}")
    print(f"Your IP maybe: {socket.gethostbyname_ex(socket.gethostname())[2]}")

    run(port=defaultPort)
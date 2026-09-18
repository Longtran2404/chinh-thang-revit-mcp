"""Exercise the actual stdio MCP process. --live additionally requires an open Revit model."""
import argparse
import json
import queue
import subprocess
import threading

parser = argparse.ArgumentParser()
parser.add_argument('server')
parser.add_argument('--live', action='store_true')
args = parser.parse_args()
proc = subprocess.Popen([args.server, '--target', '2024', '--toolsets', 'all'],
                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        text=True, encoding='utf-8', bufsize=1)
messages = queue.Queue()
def reader():
    for line in proc.stdout:
        try: messages.put(json.loads(line))
        except json.JSONDecodeError: messages.put({'unexpected_stdout': line})
threading.Thread(target=reader, daemon=True).start()
threading.Thread(target=lambda: [None for _ in proc.stderr], daemon=True).start()
def send(message):
    proc.stdin.write(json.dumps({'jsonrpc': '2.0', **message})+'\n')
    proc.stdin.flush()
def call(identifier, method, params):
    send({'id': identifier, 'method': method, 'params': params})
    while True:
        reply = messages.get(timeout=45)
        if 'unexpected_stdout' in reply: raise RuntimeError('Non-protocol stdout')
        if reply.get('id') == identifier:
            if 'error' in reply: raise RuntimeError(reply['error'])
            return reply['result']
try:
    init = call(1, 'initialize', {'protocolVersion':'2024-11-05','capabilities':{},'clientInfo':{'name':'ctmcp-smoke','version':'1.0'}})
    assert init['serverInfo']['name'] == 'chinh-thang-revit-mcp', init['serverInfo']
    send({'method':'notifications/initialized'})
    result = call(2, 'tools/list', {})
    tools = result['tools']
    assert len(tools) == 232, len(tools)
    assert any(t['name']=='revit_get_current_view_info' for t in tools)
    assert any(t['name']=='revit_send_code_to_revit' for t in tools)
    print(json.dumps({'handshake':'PASS','server':init['serverInfo'],'tool_count':len(tools)},ensure_ascii=False))
    policy = call(4, 'resources/read', {'uri':'revit://guidance/native-modeling'})
    assert any('Edit Family' in c.get('text','') for c in policy['contents']), policy
    print(json.dumps({'native_modeling_resource':'PASS'}))
    if args.live:
        view = call(3,'tools/call',{'name':'revit_get_current_view_info','arguments':{}})
        assert not view.get('isError'), view
        contents = [json.loads(c['text']) for c in view.get('content',[]) if c.get('type')=='text']
        assert contents and any('viewName' in str(c) or 'view_name' in str(c) for c in contents), view
        print(json.dumps({'live_view':'PASS','result':view},ensure_ascii=False))
finally:
    proc.terminate()
    try: proc.wait(timeout=5)
    except subprocess.TimeoutExpired: proc.kill()

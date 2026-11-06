"""Read-only, unauthenticated verification of the two published RCs."""
import hashlib,json,time,urllib.request
from pathlib import Path

EXPECTED=[
 {'repo':'mrksrus/localcorrect-browser','tag':'v1.0.0-rc.7','commit':'3a7b7aecfc628a49e2dd65f987e79bd360d67cfb','assets':{
  'localcorrect-chrome-1.0.0-rc.7.zip':(119180726,'c0ba7b57abf880f8c50508e0fe13ebe98020ad9ea74e64af71d3c8b0c8fe435f'),
  'localcorrect-firefox-1.0.0-rc.7.zip':(119180784,'449f7db3ebdc27a81a7c30731f1bc43e46cd4db83daaf632234f03767a477cf2'),
  'localcorrect-rc7-evaluation.zip':(2574135,'d48caf0aa62e86dd71b3367b8fb2064a4a2a4905f0dd0f9ec132467e3c7829e7'),
  'SHA256SUMS':(301,'c407c0184d98ee3fd574bc18412d844b37d9a2dccdf572437ef47a7f531aeb44'),
  'artifact-manifest.json':(721,'62d635ccc2a447cee0b2784008b214d53194f01bdfaef65a36c49c2f90f1c5c5')
 }},
 {'repo':'mrksrus/localcorrect-outlook','tag':'v1.0.0-rc.6','commit':'7ab5510c6d88d8361b10571a7f59ed2bbbff611c','assets':{
  'LocalCorrect-Outlook-Setup.exe':(364296704,'c367624259941235017420c5891e986c3a03d8e4731e452b2d5096d4bdb98d40'),
  'LocalCorrect-Outlook-Setup.exe.sha256':(97,'8979027e789096f23243565d312ef5feae5603fabc3db5eae1af6b18c656ad44'),
  'native-helper-benchmark.json':(2064,'211dd3cc8a7d4056e1358259fdc098ba7ee572f0cd1cd88a4833f8806cee3189'),
  'native-promotion-receipt.json':(1905,'cc1850d58cc0874e60d8a3ef8e8b18044fc65b393c7a056ed40025a8b2c80519')
 }}]
def request(url):
 # No environment token, CLI helper, Authorization or cookie is used.
 return urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'LocalCorrect-public-artifact-verification'}),timeout=90)
def api(path):
 with request('https://api.github.com/repos/'+path) as response:return json.load(response)

verified=[]
for item in EXPECTED:
 repo=item['repo'];tag=item['tag'];commit=item['commit']
 release=api(f'{repo}/releases/tags/{tag}')
 assert release['draft'] is False and release['prerelease'] is True and release['target_commitish']==commit
 assert api(f'{repo}/git/ref/tags/{tag}')['object']=={'sha':commit,'type':'commit','url':f'https://api.github.com/repos/{repo}/git/commits/{commit}'}
 assets=release['assets'];assert len(assets)==len(item['assets']) and {a['name'] for a in assets}==set(item['assets'])
 rows=[]
 for asset in assets:
  size,sha=item['assets'][asset['name']]
  assert asset['state']=='uploaded' and asset['size']==size and asset['digest']=='sha256:'+sha
  assert asset['browser_download_url'].startswith(f'https://github.com/{repo}/releases/download/{tag}/')
  actual=hashlib.sha256();count=0;started=time.monotonic()
  with request(asset['browser_download_url']) as stream:
   while chunk:=stream.read(1024*1024):actual.update(chunk);count+=len(chunk)
  assert count==size and actual.hexdigest()==sha
  row={'name':asset['name'],'bytes':count,'sha256':sha,'anonymousDownloadVerified':True,'seconds':round(time.monotonic()-started,2)}
  print(json.dumps({'repository':repo,**row}),flush=True);rows.append(row)
 verified.append({'repository':repo,'tag':tag,'commit':commit,'assets':rows})
Path('public-release-verification.json').write_text(json.dumps(verified,indent=2)+'\n')
print('Both published prereleases: all nine complete anonymous downloads match the tested hashes.',flush=True)

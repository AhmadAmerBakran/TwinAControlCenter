import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppSettings, AudioDevice, ControlService, DevProject, DevStatus, DriveInfo, FileEntry,
  FlowConfig, GameInfo, GameProfile, MqttDevice, MqttDeviceState, SettingsEnvelope, SoundInfo, SystemDetails
} from './core/control.service';

type Page='home'|'studio'|'audio'|'games'|'system'|'files'|'dev'|'iot'|'automations'|'settings';
interface Toast{title:string;detail:string;status:'success'|'warning'|'error';}

@Component({selector:'ta-root',standalone:true,imports:[CommonModule,FormsModule],templateUrl:'./app.component.html',styleUrl:'./app.component.css'})
export class AppComponent implements OnInit {
  readonly page=signal<Page>('home'); readonly toast=signal<Toast|null>(null); readonly commandBusy=signal<string|null>(null); readonly note=signal('');
  readonly activeScene=computed(()=>this.control.snapshot().scene); readonly recordingTime=computed(()=>this.formatTime(this.control.snapshot().recordingSeconds));
  readonly obsReady=computed(()=>this.control.snapshot().obs==='ready'); readonly recordingStateLabel=computed(()=>this.control.snapshot().recording?(this.control.snapshot().recordingPaused?'PAUSED':'RECORDING'):'READY');
  readonly pages:{id:Page;icon:string;label:string}[]=[{id:'home',icon:'⌂',label:'Home'},{id:'studio',icon:'◉',label:'Studio'},{id:'audio',icon:'◖',label:'Audio'},{id:'games',icon:'◇',label:'Games'},{id:'system',icon:'▣',label:'System'},{id:'files',icon:'▤',label:'Files'},{id:'dev',icon:'⌘',label:'Dev'},{id:'iot',icon:'⌁',label:'IoT'},{id:'automations',icon:'⚡',label:'Flows'},{id:'settings',icon:'⚙',label:'Settings'}];

  readonly audioDevices=signal<AudioDevice[]>([]); readonly sounds=signal<SoundInfo[]>([]); soundVolume=80;
  readonly games=signal<GameInfo[]>([]); readonly gameProfiles=signal<GameProfile[]>([]); gameEditorOpen=false; gameDraft:any=this.blankGame(); profileEditorOpen=false; profileGame:GameInfo|null=null; profileDraft:GameProfile=this.blankGameProfile('');
  readonly systemDetails=signal<SystemDetails|null>(null);
  readonly drives=signal<DriveInfo[]>([]); readonly fileEntries=signal<FileEntry[]>([]); currentPath=''; fileBusy=false;
  readonly projects=signal<DevProject[]>([]); readonly devStatus=signal<DevStatus|null>(null); devSelected=''; devOutput=''; devEditorOpen=false; devDraft:DevProject=this.blankProject();
  readonly flows=signal<FlowConfig[]>([]); flowEditorOpen=false; flowDraft:FlowConfig=this.blankFlow();
  readonly settingsEnvelope=signal<SettingsEnvelope|null>(null); readonly iotStates=signal<MqttDeviceState[]>([]); iotMessage=''; iotDeviceEditorOpen=false; iotDeviceDraft:MqttDevice=this.blankMqttDevice();

  constructor(public readonly control:ControlService){}
  async ngOnInit():Promise<void>{await this.control.connect();await this.refreshCore();}
  async go(page:Page):Promise<void>{this.page.set(page);if(page==='audio')await this.refreshAudio();if(page==='games')await this.refreshGames();if(page==='system')await this.refreshSystem();if(page==='files')await this.refreshDrives();if(page==='dev')await this.refreshDev();if(page==='iot'){await this.refreshSettings();await this.refreshIotStates();}if(page==='settings')await this.refreshSettings();if(page==='automations')await this.refreshFlows();}

  async run(command:string,payload:Record<string,unknown>={},success='Command completed'):Promise<void>{this.commandBusy.set(command);try{const r=await this.control.execute(command,payload);this.showToast(r.ok?(r.verified?success:'Executed • state unverified'):'Command failed',r.message,r.ok?(r.verified?'success':'warning'):'error');if(r.data&&(command==='dev.execute'||command==='flow.run'))this.devOutput=r.data;await this.afterCommand(command);}catch(e){this.showToast('Command failed',e instanceof Error?e.message:'Unknown error','error');}finally{this.commandBusy.set(null);}}
  async mark(kind:string):Promise<void>{await this.run('session.marker.add',{kind,note:this.note()},`${kind} marker saved`);this.note.set('');}
  async holdDanger(command:string,label:string):Promise<void>{const confirmPower=this.settingsEnvelope()?.settings.ui.confirmPowerActions??true;if(!confirmPower||window.confirm(`${label}? This action requires confirmation.`))await this.run(command,{},`${label} requested`);}
  isBusy(command:string):boolean{return this.commandBusy()===command;} setNote(value:string):void{this.note.set(value);}

  // AUDIO
  async refreshAudio():Promise<void>{try{const [devices,sounds]=await Promise.all([this.control.get<AudioDevice[]>('/api/audio/devices'),this.control.get<SoundInfo[]>('/api/sounds')]);this.audioDevices.set(devices);this.sounds.set(sounds);}catch(e){this.showToast('Audio refresh failed',this.err(e),'error');}}
  audioFor(flow:'render'|'capture'):AudioDevice[]{return this.audioDevices().filter(d=>d.flow===flow);}
  async setAudioDevice(device:AudioDevice):Promise<void>{await this.run('audio.device.set',{id:device.id,flow:device.flow},`${device.name} selected`);await this.refreshAudio();}
  async uploadSound(event:Event):Promise<void>{const input=event.target as HTMLInputElement;const file=input.files?.[0];if(!file)return;try{await this.control.uploadRaw(`/api/sounds/upload?fileName=${encodeURIComponent(file.name)}`,file);this.showToast('Sound added',file.name,'success');await this.refreshAudio();}catch(e){this.showToast('Sound upload failed',this.err(e),'error');}finally{input.value='';}}
  async deleteSound(s:SoundInfo):Promise<void>{if(!confirm(`Delete sound '${s.name}'?`))return;try{await this.control.delete(`/api/sounds/${s.id}`);await this.refreshAudio();this.showToast('Sound deleted',s.name,'success');}catch(e){this.showToast('Delete failed',this.err(e),'error');}}

  // GAMES
  async refreshGames():Promise<void>{try{const [games,profiles]=await Promise.all([this.control.get<GameInfo[]>('/api/games'),this.control.get<GameProfile[]>('/api/games/profiles')]);this.games.set(games);this.gameProfiles.set(profiles);}catch(e){this.showToast('Game scan failed',this.err(e),'error');}}
  async launchGame(game:GameInfo):Promise<void>{await this.run('game.launch',{id:game.id,name:game.name},`${game.name} launched`);await this.refreshGames();}
  openGameEditor(game?:GameInfo):void{this.gameDraft=game?.custom?{...game.config}:this.blankGame();this.gameEditorOpen=true;}
  async saveGame():Promise<void>{try{await this.control.post('/api/games/custom',this.gameDraft);this.gameEditorOpen=false;await this.refreshGames();this.showToast('Game saved',this.gameDraft.name,'success');}catch(e){this.showToast('Game save failed',this.err(e),'error');}}
  async deleteCustomGame(game:GameInfo):Promise<void>{if(!confirm(`Remove '${game.name}' from TWIN A?`))return;await this.control.delete(`/api/games/custom/${game.id}`);await this.refreshGames();}
  async openProfileEditor(game:GameInfo):Promise<void>{this.profileGame=game;if(!this.audioDevices().length)await this.refreshAudio();const existing=this.gameProfiles().find(p=>p.gameId===game.id);this.profileDraft=existing?structuredClone(existing):this.blankGameProfile(game.id);this.profileEditorOpen=true;}
  async saveGameProfile():Promise<void>{try{await this.control.post('/api/games/profile',this.profileDraft);this.profileEditorOpen=false;await this.refreshGames();this.showToast('Game profile saved',this.profileGame?.name||'Game','success');}catch(e){this.showToast('Profile save failed',this.err(e),'error');}}
  hasGameProfile(gameId:string):boolean{return this.gameProfiles().some(p=>p.gameId===gameId);}
  async deleteGameProfile():Promise<void>{if(!this.profileGame||!this.hasGameProfile(this.profileGame.id))return;if(!confirm(`Remove automation profile for '${this.profileGame.name}'?`))return;try{await this.control.delete(`/api/games/profile/${encodeURIComponent(this.profileGame.id)}`);this.profileEditorOpen=false;await this.refreshGames();this.showToast('Profile removed',this.profileGame.name,'success');}catch(e){this.showToast('Profile removal failed',this.err(e),'error');}}

  // SYSTEM
  async refreshSystem():Promise<void>{try{this.systemDetails.set(await this.control.get<SystemDetails>('/api/system/details'));}catch(e){this.showToast('System refresh failed',this.err(e),'error');}}

  // FILES
  async refreshDrives():Promise<void>{try{this.drives.set(await this.control.get<DriveInfo[]>('/api/files/drives'));if(!this.currentPath&&this.drives().length)await this.openPath(this.drives()[0].name);else if(this.currentPath)await this.openPath(this.currentPath);}catch(e){this.showToast('Drive scan failed',this.err(e),'error');}}
  async openPath(path:string):Promise<void>{this.fileBusy=true;try{this.fileEntries.set(await this.control.get<FileEntry[]>(`/api/files/browse?path=${encodeURIComponent(path)}`));this.currentPath=path;}catch(e){this.showToast('Cannot open folder',this.err(e),'error');}finally{this.fileBusy=false;}}
  async fileOpen(entry:FileEntry):Promise<void>{if(entry.isDirectory){await this.openPath(entry.fullPath);return;}await this.fileOpenOnPc(entry);}
  async fileOpenOnPc(entry:FileEntry):Promise<void>{await this.run('files.open',{path:entry.fullPath},`${entry.isDirectory?'Folder':'File'} open request sent`);}
  async fileUp():Promise<void>{if(!this.currentPath||/^[A-Za-z]:[\\/]?$/.test(this.currentPath))return;const trimmed=this.currentPath.replace(/[\\/]+$/,'');const idx=Math.max(trimmed.lastIndexOf('\\'),trimmed.lastIndexOf('/'));const parent=idx>2?trimmed.slice(0,idx):trimmed.slice(0,3)+'\\';if(parent&&parent!==this.currentPath)await this.openPath(parent);}
  async createFolder():Promise<void>{const name=prompt('New folder name');if(!name)return;await this.run('files.folder.create',{parent:this.currentPath,name},'Folder created');await this.openPath(this.currentPath);}
  async renameFile(entry:FileEntry):Promise<void>{const newName=prompt('New name',entry.name);if(!newName||newName===entry.name)return;await this.run('files.rename',{path:entry.fullPath,newName},'Item renamed');await this.openPath(this.currentPath);}
  async deleteFile(entry:FileEntry):Promise<void>{if(!confirm(`Delete ${entry.isDirectory?'folder':'file'} '${entry.name}'${entry.isDirectory?' and everything inside it':''}?`))return;await this.run('files.delete',{path:entry.fullPath},'Item deleted');await this.openPath(this.currentPath);}
  async copyFile(entry:FileEntry):Promise<void>{const destination=prompt('Copy to folder',this.currentPath);if(!destination)return;await this.run('files.copy',{path:entry.fullPath,destination},'Item copied');await this.openPath(this.currentPath);}
  async moveFile(entry:FileEntry):Promise<void>{const destination=prompt('Move to folder',this.currentPath);if(!destination)return;await this.run('files.move',{path:entry.fullPath,destination},'Item moved');await this.openPath(this.currentPath);}
  downloadFile(entry:FileEntry):void{window.open(this.control.downloadUrl(entry.fullPath),'_blank');}
  async uploadFile(event:Event):Promise<void>{const input=event.target as HTMLInputElement;const file=input.files?.[0];if(!file||!this.currentPath)return;try{await this.control.uploadRaw(`/api/files/upload?directory=${encodeURIComponent(this.currentPath)}&fileName=${encodeURIComponent(file.name)}`,file);await this.openPath(this.currentPath);this.showToast('Upload verified',file.name,'success');}catch(e){this.showToast('Upload failed',this.err(e),'error');}finally{input.value='';}}

  // DEV
  async refreshDev():Promise<void>{try{this.projects.set(await this.control.get<DevProject[]>('/api/dev/projects'));if(!this.devSelected&&this.projects().length)this.devSelected=this.projects()[0].id;if(this.devSelected)await this.loadDevStatus();}catch(e){this.showToast('Dev refresh failed',this.err(e),'error');}}
  selectedProject():DevProject|undefined{return this.projects().find(p=>p.id===this.devSelected);}
  flowNumber(i:number):string{return(i+1).toString().padStart(2,'0');}
  async loadDevStatus():Promise<void>{if(!this.devSelected)return;try{this.devStatus.set(await this.control.get<DevStatus>(`/api/dev/projects/${this.devSelected}/status`));}catch(e){this.showToast('Project status failed',this.err(e),'error');}}
  async devAction(action:string):Promise<void>{if(!this.devSelected)return;const r=await this.control.execute('dev.execute',{id:this.devSelected,action});this.showToast(r.ok?(r.verified?`${action} completed`:'Action started'):'Action failed',r.message,r.ok?(r.verified?'success':'warning'):'error');this.devOutput=r.data||r.message;await this.loadDevStatus();}
  openDevEditor(project?:DevProject):void{this.devDraft=project?structuredClone(project):this.blankProject();this.devEditorOpen=true;}
  async saveDevProject():Promise<void>{await this.control.post('/api/dev/projects',this.devDraft);this.devEditorOpen=false;await this.refreshDev();this.showToast('Project saved',this.devDraft.name,'success');}
  async deleteDevProject(p:DevProject|undefined):Promise<void>{if(!p)return;if(p.id==='twina'){this.showToast('Protected project','TWIN A is the built-in project and cannot be removed.','warning');return;}if(!confirm(`Remove ${p.name}?`))return;await this.control.delete(`/api/dev/projects/${p.id}`);this.devSelected='';await this.refreshDev();}

  // FLOWS
  async refreshFlows():Promise<void>{try{this.flows.set(await this.control.get<FlowConfig[]>('/api/flows'));}catch(e){this.showToast('Flows refresh failed',this.err(e),'error');}}
  async runFlow(flow:FlowConfig):Promise<void>{const r=await this.control.execute('flow.run',{id:flow.id});this.showToast(r.ok?(r.verified?'Flow verified':'Flow completed with warnings'):'Flow failed',r.message,r.ok?(r.verified?'success':'warning'):'error');this.devOutput=r.data||'';}
  openFlowEditor(flow?:FlowConfig):void{this.flowDraft=flow?structuredClone(flow):this.blankFlow();this.flowEditorOpen=true;}
  addFlowStep():void{this.flowDraft.steps.push({command:'app.launch',payload:{app:'obs'},delayAfterMs:0,continueOnError:false});}
  removeFlowStep(i:number):void{this.flowDraft.steps.splice(i,1);}
  payloadText(step:any):string{return Object.entries(step.payload||{}).map(([k,v])=>`${k}=${v}`).join('; ');}
  setPayloadText(step:any,value:string):void{const payload:Record<string,string>={};for(const pair of value.split(';')){const idx=pair.indexOf('=');if(idx>0)payload[pair.slice(0,idx).trim()]=pair.slice(idx+1).trim();}step.payload=payload;}
  async saveFlow():Promise<void>{await this.control.post('/api/flows',this.flowDraft);this.flowEditorOpen=false;await this.refreshFlows();this.showToast('Flow saved',this.flowDraft.name,'success');}
  async deleteFlow(flow:FlowConfig):Promise<void>{if(!confirm(`Delete flow '${flow.name}'?`))return;await this.control.delete(`/api/flows/${flow.id}`);await this.refreshFlows();}

  // SETTINGS + IOT
  async refreshSettings():Promise<void>{try{this.settingsEnvelope.set(await this.control.get<SettingsEnvelope>('/api/settings'));}catch(e){this.showToast('Settings failed',this.err(e),'error');}}
  async saveSettings():Promise<void>{const env=this.settingsEnvelope();if(!env)return;try{await this.control.put('/api/settings',env.settings);await this.refreshSettings();this.showToast('Settings saved',`Configuration written to ${this.settingsEnvelope()?.configPath}`,'success');}catch(e){this.showToast('Settings save failed',this.err(e),'error');}}
  async testIot():Promise<void>{try{const r:any=await this.control.post('/api/iot/test',{});this.iotMessage=r.message;this.showToast(r.verified?'MQTT verified':'MQTT connected',r.message,r.verified?'success':'warning');await this.refreshIotStates();}catch(e){this.iotMessage=this.err(e);this.showToast('MQTT connection failed',this.iotMessage,'error');}}
  async refreshIotStates():Promise<void>{try{this.iotStates.set(await this.control.get<MqttDeviceState[]>('/api/iot/states'));}catch{this.iotStates.set([]);}}
  iotState(id:string):MqttDeviceState|undefined{return this.iotStates().find(s=>s.id===id);}
  openIotDeviceEditor(device?:MqttDevice):void{this.iotDeviceDraft=device?structuredClone(device):this.blankMqttDevice();this.iotDeviceEditorOpen=true;}
  async saveIotDevice():Promise<void>{const env=this.settingsEnvelope();if(!env)return;const d=this.iotDeviceDraft;if(!d.name.trim()){this.showToast('Device not saved','Name is required.','error');return;}if(!d.id)d.id=crypto.randomUUID().replaceAll('-','');const devices=env.settings.mqtt.devices;const i=devices.findIndex(x=>x.id===d.id);if(i>=0)devices[i]=structuredClone(d);else devices.push(structuredClone(d));await this.saveSettings();this.iotDeviceEditorOpen=false;await this.refreshIotStates();this.showToast('IoT device saved',d.name,'success');}
  async deleteIotDevice(device:MqttDevice):Promise<void>{if(!confirm(`Remove IoT device '${device.name}'?`))return;const env=this.settingsEnvelope();if(!env)return;env.settings.mqtt.devices=env.settings.mqtt.devices.filter(d=>d.id!==device.id);await this.saveSettings();await this.refreshIotStates();}
  async toggleIotDevice(device:MqttDevice,on:boolean):Promise<void>{await this.run('iot.toggle',{id:device.id,on},`${device.name} ${on?'ON':'OFF'}`);await this.refreshIotStates();}
  setMqtt(field:'enabled'|'host'|'port'|'tls'|'username',value:any):void{const env=this.settingsEnvelope();if(!env)return;(env.settings.mqtt as any)[field]=field==='port'?Number(value):value;this.settingsEnvelope.set({...env,settings:{...env.settings,mqtt:{...env.settings.mqtt}}});}
  setUi(field:'confirmPowerActions'|'protectSystemPaths',value:boolean):void{const env=this.settingsEnvelope();if(!env)return;(env.settings.ui as any)[field]=value;this.settingsEnvelope.set({...env,settings:{...env.settings,ui:{...env.settings.ui}}});}

  sourceIcon(name:string):string{const v=name.toLowerCase();if(v.includes('mic'))return'🎙';if(v.includes('desktop'))return'◖';if(v.includes('game'))return'◇';return'◉';}
  formatDb(value:number):string{if(!Number.isFinite(value))return'—';const r=Math.round(value*10)/10;return`${r>0?'+':''}${r.toFixed(1)} dB`;}
  formatBytes(n:number):string{if(!Number.isFinite(n))return'—';const u=['B','KB','MB','GB','TB'];let i=0;while(n>=1024&&i<u.length-1){n/=1024;i++;}return`${n<10&&i>0?n.toFixed(1):Math.round(n)} ${u[i]}`;}
  formatUptime(value:string|undefined):string{if(!value)return'—';const match=value.match(/(?:(\d+)\.)?(\d+):(\d+)/);if(!match)return value;return`${match[1]?match[1]+'d ':''}${match[2]}h ${match[3]}m`;}

  private async refreshCore():Promise<void>{await Promise.allSettled([this.refreshSettings(),this.refreshSystem()]);}
  private async afterCommand(command:string):Promise<void>{if(command.startsWith('audio.')||command.startsWith('sound.'))await this.refreshAudio();if(command.startsWith('game.'))await this.refreshGames();if(command.startsWith('files.'))await this.refreshDrives();if(command.startsWith('dev.'))await this.refreshDev();if(command.startsWith('iot.'))await this.refreshIotStates();}
  private showToast(title:string,detail:string,status:'success'|'warning'|'error'):void{this.toast.set({title,detail,status});window.setTimeout(()=>this.toast.set(null),3600);}
  private err(e:unknown):string{const any=e as any;return any?.error?.message||any?.error?.detail||any?.message||'Unknown error';}
  private formatTime(seconds:number):string{const h=Math.floor(seconds/3600).toString().padStart(2,'0');const m=Math.floor((seconds%3600)/60).toString().padStart(2,'0');const s=Math.floor(seconds%60).toString().padStart(2,'0');return`${h}:${m}:${s}`;}
  private blankGame():any{return{id:'',name:'',launchTarget:'',arguments:'',processName:'',workingDirectory:'',obsScene:'',startReplayBuffer:false,startRecording:false};}
  private blankGameProfile(gameId:string):GameProfile{return{gameId,launchObs:false,launchDiscord:false,audioOutputDeviceId:null,obsScene:null,ensureReplayBuffer:false,ensureRecording:false,masterVolume:null};}
  private blankProject():DevProject{return{id:'',name:'',workingDirectory:'',solutionOrProject:'',buildCommand:'',testCommand:'',runCommand:'',idePath:'%LOCALAPPDATA%\\Programs\\Rider\\bin\\rider64.exe'};}
  private blankFlow():FlowConfig{return{id:'',name:'New flow',category:'CUSTOM',steps:[]};}
  private blankMqttDevice():MqttDevice{return{id:'',name:'',stateTopic:'',commandTopic:'',onPayload:'ON',offPayload:'OFF'};}
}

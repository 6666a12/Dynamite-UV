extends Node
## Test-only autoload; emits native viewport events, observes live formal nodes/pixels.
## Never loads/substitutes a page, sets a product field, invokes a product callback, or captures the desktop.
var out:=OS.get_environment('DU_QA_OUT')
var suite:=OS.get_environment('DU_QA_SUITE')
var failed:=false
var trail:Array=[]
var t:=0.
var mark_at:=Vector2(-400,-400)
var pressed:=false
var pointer:Control
var formal_covers:=0
var last_cover:=false
func _ready():
 process_mode=Node.PROCESS_MODE_ALWAYS
 var real:=OS.get_user_data_dir().simplify_path().replace('\\','/').to_lower();var want:=OS.get_environment('DU_QA_EXPECT_USER').simplify_path().replace('\\','/').to_lower()
 if real!=want or not real.contains('/.tmp/production-migration-qa/runs/') or DirAccess.dir_exists_absolute('res://testdata'):
  print('MIGRATION QA FAIL: unsafe user/project root; actual=',real,' expected=',want);get_tree().quit(12);return
 var layer:=CanvasLayer.new();layer.layer=95;add_child(layer);pointer=Control.new();pointer.mouse_filter=Control.MOUSE_FILTER_IGNORE;pointer.size=Vector2(1920,1080);layer.add_child(pointer);pointer.draw.connect(_pointer_ink)
 call_deferred('run_tour')
func _pointer_ink():
 pointer.draw_arc(mark_at,12.,0.,TAU,36,Color(1,1,1,.85),1.7,true);if pressed:pointer.draw_circle(mark_at,8.,Color(1,1,1,.30))
func _process(d):
 t+=d;if pointer!=null:pointer.queue_redraw()
 var nav=get_node_or_null('/root/TransitionDirector')
 if nav!=null and nav.get_child_count()>0:
  var layer=nav.get_child(0);if layer.get_child_count()>0:
   var shield=layer.get_child(0);var covered:bool=shield is CanvasItem and shield.visible
   if covered and not last_cover:formal_covers+=1
   last_cover=covered
func wait_s(s:float):await get_tree().create_timer(s,true,false,false).timeout
func scene(id:String)->bool:return get_tree().current_scene!=null and get_tree().current_scene.scene_file_path=='res://scenes/'+id+'.tscn'
func require(what:bool,why:String)->bool:
 if failed:return false
 trail.append({'check':why,'pass':what,'time':t});print('CHECK ',why,': ',what)
 if not what:
  failed=true;report(false);push_error('MIGRATION QA FAIL: '+why);get_tree().quit(2)
 return what
func report(ok):
 var f:=FileAccess.open(out+'/result.json',FileAccess.WRITE);if f!=null:f.store_string(JSON.stringify({'suite':suite,'pass':ok,'route':str(get_tree().current_scene.scene_file_path if get_tree().current_scene!=null else ''),'formalCoverEntries':formal_covers,'evidence':trail},'  '));f.close()
func shot(id:String):
 await RenderingServer.frame_post_draw
 var im:=get_viewport().get_texture().get_image();if not require(im!=null and not im.is_empty(),'viewport '+id):return
 im.save_png(out+'/frames/'+id+'.png');trail.append({'frame':id,'time':t})
func active_page(id:String):
 for i in 600:
  if scene(id) and not get_tree().paused and not last_cover:break
  await get_tree().process_frame
 await wait_s(.82);return require(scene(id) and not last_cover and not get_tree().paused,'arrived and input unlocked: '+id)
func mb(p:Vector2,v:bool):
 var e:=InputEventMouseButton.new();e.device=1;e.position=p;e.global_position=p;e.button_index=MOUSE_BUTTON_LEFT;e.pressed=v;e.button_mask=MOUSE_BUTTON_MASK_LEFT if v else 0;get_viewport().push_input(e,true);mark_at=p;pressed=v
func move(p:Vector2):
 var e:=InputEventMouseMotion.new();e.device=1;e.position=p;e.global_position=p;e.relative=p-mark_at;e.button_mask=MOUSE_BUTTON_MASK_LEFT if pressed else 0;get_viewport().push_input(e,true);mark_at=p
func touch_at(index:int,p:Vector2,down:bool):
 var event:=InputEventScreenTouch.new()
 event.index=index;event.position=p;event.pressed=down
 get_viewport().push_input(event,true)
func click(p:Vector2):
 move(p);await wait_s(.10);mb(p,true);await wait_s(.08);mb(p,false);await wait_s(.12)
func key(code:int,ctrl:=false,unicode:=0):
 for down in [true,false]:
  var e:=InputEventKey.new();e.device=1;e.pressed=down;e.keycode=code;e.physical_keycode=code;e.ctrl_pressed=ctrl;e.unicode=unicode if down else 0;get_viewport().push_input(e,true)
func enter(edit:LineEdit,value:String,submit:=false):
 await click(edit.get_global_rect().get_center());if not require(edit.has_focus(),'native input received focus'):return
 if suite=='palette' and not edit.secret:
  print('QA HEX environment: window focus=',get_window().has_focus(),' focus owner=',get_viewport().gui_get_focus_owner(),' control=',edit,' can_process=',edit.can_process(),' paused=',get_tree().paused,' visible=',edit.is_visible_in_tree())
  edit.gui_input.connect(func(ev):
   if ev is InputEventKey:print('QA HEX GUI KEY ',ev.as_text(),' unicode=',ev.unicode,' down=',ev.pressed,' buf=',edit.text))
  edit.text_changed.connect(func(txt):print('QA HEX changed=',txt))
 # UI actions map to the default keyboard. Do not reuse synthetic pointer IDs.
 var keyboard=func(code:int,ctrl:=false,u:=0):
  for down in [true,false]:
   var e:=InputEventKey.new();e.device=0;e.pressed=down;e.keycode=code;e.physical_keycode=code;e.ctrl_pressed=ctrl;e.unicode=u if down else 0;get_viewport().push_input(e,true)
 if not edit.is_editing():keyboard.call(KEY_ENTER);await get_tree().process_frame
 keyboard.call(KEY_A,true)
 if suite=='palette':print('QA HEX focus=',edit.has_focus(),' editing=',edit.is_editing(),' initial=',edit.text,' selected=',edit.get_selected_text());await get_tree().process_frame
 if suite=='palette' and edit.get_selected_text().is_empty():
  # Real text selection; never touch LineEdit.text / clipboard or prod call sites.
  var rr:=edit.get_global_rect();var rr_y:=rr.get_center().y;var right:=Vector2(rr.end.x-4,rr_y);var left:=Vector2(rr.position.x+1,rr_y)
  move(right);mb(right,true)
  for j in range(1,13):move(right.lerp(left,float(j)/12.));await get_tree().process_frame
  mb(left,false);await wait_s(.06);print('QA native mouse selection=',edit.get_selected_text(),' size=',edit.size,' max=',edit.max_length)
 keyboard.call(KEY_BACKSPACE);await get_tree().process_frame
 if suite=='palette':print('QA after delete=',edit.text,' handled=',get_viewport().is_input_handled())
 for ch in value:
  keyboard.call(ch.to_upper().unicode_at(0),false,ch.unicode_at(0));await get_tree().process_frame
 if suite=='palette':print('QA HEX expected=',value,' actual=',edit.text,' focus=',edit.has_focus(),' cursor=',edit.caret_column,' editable=',edit.editable)
 if not require(edit.text==value,'native input accepted intended text'):return
 if submit:keyboard.call(KEY_ENTER);await wait_s(.15)
func named(id:String):return get_tree().current_scene.find_child(id,true,false)
func visible_id(id:String)->bool:
 var n=named(id);return n is CanvasItem and n.is_visible_in_tree()
func click_id(id:String)->bool:
 var n=named(id);if not require(n is Control and n.is_visible_in_tree(),'visible control '+id):return false
 await click(n.get_global_rect().get_center());return true
func wait_release(id:String)->bool:
 for i in 350:
  if named(id)!=null and (visible_id(id) or visible_id('StoreExpanded')):return true
  await get_tree().process_frame
 return require(false,'real release identity absent: '+id)
func edits()->Array:
 var a:Array=[];for c in get_tree().current_scene.find_children('*','LineEdit',true,false):
  if c.is_visible_in_tree():a.append(c)
 a.sort_custom(func(l,r):return l.global_position.y<r.global_position.y);return a
func pixel_diffs(im:Image,box:Rect2i,offset:Vector2i,other:Image=null)->int:
 var count:=0;var source:Image=im if other==null else other
 var sz:=Vector2(im.get_size())/Vector2(1920,1080)
 for y in range(box.position.y,box.end.y,2):
  for x in range(box.position.x,box.end.x,2):
   var a:Color=im.get_pixelv(Vector2i(Vector2(x,y)*sz));var b:Color=source.get_pixelv(Vector2i(Vector2(Vector2i(x,y)+offset)*sz));if absf(a.r-b.r)+absf(a.g-b.g)+absf(a.b-b.b)>.12:count+=1
 return count
func palette():
 if visible_id('HomeSettings'):await click_id('HomeSettings')
 else:await click(Vector2(588,792))
 if not await active_page('settings'):return
 if visible_id('SettingsSections'):
  var area:Control=named('SettingsSections');var q:=area.get_global_rect().get_center();move(q);mb(q,true)
  for j in range(1,12):move(q+Vector2(0,-10*j));await wait_s(.025)
  mb(q+Vector2(0,-110),false);await wait_s(.2)
  if visible_id('SettingsTabEffects'):await click_id('SettingsTabEffects')
  else:await click(Vector2(247,671))
 else:await click(Vector2(200,550))
 await wait_s(.35)
 if visible_id('SettingsColorsEntry'):await click_id('SettingsColorsEntry')
 else:await click(Vector2(1708,946))
 if not await active_page('note_colors'):return
 if not require(formal_covers>=2,'two formal TransitionDirector cover/reveal cycles'):return
 if not await click_id('SpecimenPick0'):return
 var frame=named('SpecimenPick3');if not require(frame is Control,'live HOLD pick target'):return
 # Press on the top edge (not over the sample); sample two remote stretches of that edge.
 # Use the non-pressed cursor in both readbacks: marker is outside the measured stripes.
 var fr:Rect2=frame.get_global_rect();var edge:=fr.position+Vector2(fr.size.x*.5,4);move(edge);await wait_s(.35);await RenderingServer.frame_post_draw;var before:=get_viewport().get_texture().get_image();mb(edge,true);await wait_s(.09);await shot('01-hold-contact');var after:=get_viewport().get_texture().get_image();mb(edge,false);await wait_s(.33)
 var pigment:=pixel_diffs(before,Rect2i(540,655,74,11),Vector2i.ZERO,after);var et:=Vector2i(edge);var rim:=pixel_diffs(before,Rect2i(et.x-82,et.y-3,60,8),Vector2i.ZERO,after)+pixel_diffs(before,Rect2i(et.x+22,et.y-3,60,8),Vector2i.ZERO,after);trail.append({'outerRimChangedAwayFromFinger':rim,'innerInkChanged':pigment});if not require(rim>=3 and pigment<12,'contact visibly travels on outer rim while HOLD pigment stays stable'):return
 var es:=edits();if not require(es.size()==1,'native HEX editor exists'):return
 await enter(es[0],'F65C95',true);if failed:return
 await shot('02-desk-draft');await click(Vector2(1720,1014));await wait_s(.42)
 var saved=JSON.parse_string(FileAccess.get_file_as_string('user://settings.json'))
 if not require(saved is Dictionary and str(saved.get('notePalettes',[])).to_upper().contains('F65C95'),'Apply persisted colour inside QA user://'):return
 await click(Vector2(1038,188));await wait_s(.3);await shot('03-observe-draft');var draft:=get_viewport().get_texture().get_image();var box:=Rect2i(135,414,121,85);var diff:=pixel_diffs(draft,box,Vector2i(296,0));if not require(diff>30,'same background original vs edited note physically differ'):return
 mb(Vector2(184,1014),true);await wait_s(.48);await shot('04-compare-held');var equal:=pixel_diffs(get_viewport().get_texture().get_image(),box,Vector2i(296,0));if not require(equal<maxi(15,diff/5),'held comparison restores entry scheme even after Apply'):return
 mb(Vector2(184,1014),false);await wait_s(.3);await shot('05-compare-released');if not require(pixel_diffs(get_viewport().get_texture().get_image(),box,Vector2i(296,0))>30,'release restores edited pigment'):return
 await click(Vector2(1720,1014));await wait_s(.35);if not require(edits().size()==1,'observer returned to editor'):return
 key(KEY_ESCAPE);if not await active_page('settings'):return
 if visible_id('SettingsBack'):await click_id('SettingsBack')
 else:await click(Vector2(100,85))
 if not await active_page('main'):return
func playing()->bool:
 for a in get_tree().current_scene.find_children('*','AudioStreamPlayer',true,false):
  if a.playing and a.stream!=null and not a.stream_paused:return true
 return false
func selected_song_row()->Control:
 for node in get_tree().current_scene.find_children('*','Control',true,false):
  if node.accessibility_description=='当前选择':return node as Control
 return null
func long_hold_standard(bleed:Control)->bool:
 var standard:=named('ModeStandard') as Control
 if not require(standard!=null,'native STANDARD control exists'):return false
 var rim:=standard.get_node_or_null('ContactRim')
 if not require(rim!=null,'STANDARD has a contact rim'):return false
 var p:=standard.get_global_rect().get_center()
 touch_at(11,p,true)
 if not require(rim.is_processing(),'first touch starts the outer rim'):return false
 await wait_s(.63)
 if not require(not rim.is_processing() and bleed.accessibility_name.contains('锁定'),'idle rim during held touch; no premature mode change'):return false
 touch_at(12,p,true);touch_at(12,p,false)
 if not require(not rim.is_processing() and bleed.accessibility_name.contains('锁定'),'second touch cannot take over an expired rim'):return false
 touch_at(11,p,false)
 return require(rim.is_processing() and not bleed.accessibility_name.contains('锁定'),'original release restarts rim and commits STANDARD')
func revealed_row_is_highlighted(scroll:ScrollContainer,row:Control)->bool:
 var rect:=row.get_global_rect()
 var point:=Vector2(rect.position.x+4,rect.get_center().y)
 if not require(scroll.get_global_rect().has_point(point),'revealed selected row is within the list'):return false
 await RenderingServer.frame_post_draw
 var image:=get_viewport().get_texture().get_image()
 if not require(image!=null and not image.is_empty(),'captured selected row pixels'):return false
 var scale:=Vector2(image.get_size())/Vector2(1920,1080)
 var strip:=image.get_pixelv(Vector2i((point+Vector2(1,0))*scale))
 var body:=image.get_pixelv(Vector2i((point+Vector2(12,0))*scale))
 return require(strip.b-body.b>.25,'revealed selected row has its violet marker without hovering')
func library():
 await shot('00-home-station');await click(Vector2(350,586));if not await active_page('song_select'):return
 var c=named('ExpandedCard');if not require(c is Control and not c.is_visible_in_tree() and not playing(),'library initially collapsed and silent'):return
 var starting_selection:=selected_song_row()
 var rows=get_tree().current_scene.find_children('*','ScrollContainer',true,false);var scroll:ScrollContainer=null
 for candidate in rows:
  if candidate.size.x>1000:scroll=candidate;break
 if not require(scroll!=null and scroll.get_v_scroll_bar().max_value>scroll.get_v_scroll_bar().page,'scrollable fixture has real overflow'):return
 var was:=scroll.scroll_vertical
 var start:=Vector2(1060,382);var finish:=start+Vector2(3,-103);var finger:=InputEventScreenTouch.new();finger.index=7;finger.position=start;finger.pressed=true;get_viewport().push_input(finger,true);mark_at=start;pressed=true
 var prev:=start
 for i in range(1,17):
  var p:=start.lerp(finish,float(i)/16.);var drag:=InputEventScreenDrag.new();drag.position=p;drag.relative=p-prev;drag.screen_relative=drag.relative;drag.index=7;get_viewport().push_input(drag,true);mark_at=p;prev=p;await wait_s(.025)
 finger=InputEventScreenTouch.new();finger.position=finish;finger.index=7;finger.pressed=false;get_viewport().push_input(finger,true);pressed=false;await wait_s(.3)
 if not require(absi(scroll.scroll_vertical-was)>35 and not c.is_visible_in_tree() and not playing(),'true material scroll: no selection or playback'):return
 await shot('01-list-after-drag');await click(Vector2(900,470))
 for i in 240:
  if c.is_visible_in_tree() and playing():break
  await get_tree().process_frame
 if not require(c.is_visible_in_tree() and playing(),'click projected row expands and starts real decoded audio'):return
 var light=named('PreviewRailCurrent');if not require(light!=null and light.is_processing(),'first real preview starts the rail current'):return
 await wait_s(.42);await shot('02-expanded-preview');await wait_s(1.45)
 var transport:Vector2=c.get_global_rect().position+Vector2(389,276);await click(transport)
 if not require(not playing() and light.is_processing(),'accepted pause sends exactly one rail indication'):return
 await wait_s(.18);await shot('02d-preview-current');var lit:=get_viewport().get_texture().get_image();await wait_s(.78);await RenderingServer.frame_post_draw
 var stable:=get_viewport().get_texture().get_image();var rail_box:=Rect2i(Vector2i(c.global_position)+Vector2i(426,261),Vector2i(740,58))
 var changed:=pixel_diffs(lit,rail_box,Vector2i.ZERO,stable);trail.append({'pausedRailChangedSamples':changed});if not require(changed>12 and not light.is_processing(),'visible pulse on the paused track decays without restarting'):return
 await click(transport);if not require(playing() and light.is_processing(),'resume relights rail and playback continues'):return
 if not await click_id('ModeHardcore'):return
 await wait_s(.11);await shot('02b-channel-hardcore');var ble=named('ChannelBleed') as Control;if not require(ble!=null and ble.accessibility_name.contains('锁定'),'Hardcore forces BLEED immediately'):return
 await click(ble.get_global_rect().get_center());if not require(ble.accessibility_name.contains('锁定'),'locked control cannot switch off'):return
 if not await long_hold_standard(ble):return
 await wait_s(.46);var r:Rect2=c.get_global_rect();var clip:Rect2=r.intersection(scroll.get_global_rect());var tap:=clip.position+Vector2(128,clip.size.y*.5);print('card rect=',r,' visible clip=',clip,' physical tap=',tap);await click(tap)
 move(Vector2(540,925));await wait_s(.52)
 print('after fold card=',c.is_visible_in_tree(),' playing=',playing());await shot('02c-after-fold');if not require(not c.is_visible_in_tree() and not playing(),'click expanded body collapses and stops audio'):return
 var revealed:=selected_song_row()
 if not require(revealed!=null and revealed!=starting_selection and revealed.is_visible_in_tree(),'fold reveals the newly selected song row'):return
 if not await revealed_row_is_highlighted(scroll,revealed):return
 await shot('03-collapsed');if not await click_id('SongBack'):return
 if not await active_page('main'):return
func picker_edit()->LineEdit:
 for e in edits():
  if e.placeholder_text.begins_with('https'):return e
 return null
func discover_confirm():
 var address:=picker_edit();if not require(address!=null,'real server identity picker'):return
 await enter(address,OS.get_environment('DU_QA_URL'));await click(Vector2(680,550))
 var confirm:Button=null
 for i in 240:
  for b in get_tree().current_scene.find_children('*','Button',true,false):
   if b.is_visible_in_tree() and not b.disabled and b.text.contains('确认此服务器身份'):confirm=b
  if confirm!=null:break
  await get_tree().process_frame
 if not require(confirm!=null,'descriptor discovered; explicit identity confirmation enabled'):return
 await click(confirm.get_global_rect().get_center());await wait_s(.25)
func sign_in():
 var es:=edits();if not require(es.size()==2 and es[1].secret,'confirmed identity before account credentials'):return
 await enter(es[0],OS.get_environment('DU_QA_USER'));await enter(es[1],OS.get_environment('DU_QA_PASS'));await click(Vector2(1330,772))
func store():
 await click(Vector2(350,733));if not await active_page('online'):return
 if not require(picker_edit()!=null,'fresh store intent opens picker automatically'):return
 key(KEY_ESCAPE);await wait_s(.3);if not require(scene('online') and picker_edit()==null,'first Escape dismisses server picker and cancels pending store'):return
 # Affirm cancellation by signing in on that same page: a valid response must NOT take us to store.
 await shot('00-account-pass');if not await click_id('OnlineSwitchServer'):return
 await discover_confirm();if failed:return
 await sign_in();if failed:return
 var signout:Button=null
 for i in 300:
  if not scene('online'):break
  for b in get_tree().current_scene.find_children('*','Button',true,false):
   if b.text=='退出登录' and b.is_visible_in_tree() and not b.disabled:signout=b
  if signout!=null:break
  await get_tree().process_frame
 await wait_s(.55);if not require(scene('online') and signout!=null,'cancelled store intent does not redirect even after real successful login'):return
 await click(signout.get_global_rect().get_center());await wait_s(.4);key(KEY_ESCAPE);if not await active_page('main'):return
 await click(Vector2(350,733));if not await active_page('online'):return
 if picker_edit()!=null:
  await discover_confirm();if failed:return
 await sign_in();if failed:return
 if not await active_page('store'):return
 var row:='StoreRelease_134ecc9a759a4df4b1c560af3bbba800';if not await wait_release(row):return
 if not visible_id('StoreExpanded'):
  if not await click_id(row):return
 await wait_s(.45);await shot('01-loopback-release');if not await click_id('StoreFilterInstalled'):return
 await wait_s(.3);if not require(visible_id('StoreEmpty') and visible_id('StoreShowAll') and not visible_id('StoreExpanded') and not visible_id('StorePrimary') and not visible_id(row),'empty installed: no stale release or CTA'):return
 await shot('02-empty-installed');if not await click_id('StoreShowAll'):return
 if not await wait_release(row):return
 await wait_s(.45);await shot('03-restored-catalog');if not require(not visible_id('StoreEmpty') and not visible_id('StoreShowAll'),'all restores real release'):return
 if not require(visible_id('StorePrimary') and not named('StorePrimary').disabled,'restored selection offers its real download'):return
 await click_id('StorePrimary');var installed:=false
 for j in 450:
  if named('StorePrimary').text=='前往本地选曲':installed=true;break
  await get_tree().process_frame
 if not require(installed,'archive downloaded, verified and published to app-owned installer'):return
 await shot('04-downloaded');await click_id('StorePrimary');if not await active_page('song_select'):return
 var chosen:Control=null
 for node in get_tree().current_scene.find_children('*','Control',true,false):
  if node.accessibility_name=='Synthetic Pulse' and node.accessibility_description=='当前选择':chosen=node;break
 if not require(chosen!=null and not visible_id('ExpandedCard') and not playing(),'exact installed release selected on return, original silent entry retained'):return
 await click(chosen.get_global_rect().get_center());await wait_s(.5);if not require(visible_id('ExpandedCard') and playing(),'installed song opens and plays only after explicit tap'):return
 await shot('05-installed-in-library')
func calibration():
 await click(Vector2(350,875));if not await active_page('settings'):return
 await shot('00-settings-gameplay');if not await click_id('SettingsTabAudio'):return
 await wait_s(.7);var slider=named('MusicSlider');var rr:Rect2=slider.get_global_rect();await click(rr.position+rr.size*Vector2(.33,.5));await wait_s(.18)
 var saved=JSON.parse_string(FileAccess.get_file_as_string('user://settings.json'));if not require(saved is Dictionary and int(saved.get('musicVolume',80))<55,'real music bus slider persisted a changed gain'):return
 await shot('01-audio-bus');await click_id('MonitorMusic');await wait_s(.58)
 await click(Vector2(1686,986));if not await active_page('calibration'):return
 await shot('02-calibration-ready');await click(Vector2(620,600));await wait_s(.4)
 var counter=named('CalibrationSampleCount');if not require(counter!=null and int(counter.text)==0 and named('CalibrationPhaseStatus').text!='等待开始','real original strike area starts sampling, zero warmup samples'):return
 var accepted:=false
 for j in 180:
  mb(Vector2(840,897),true);await get_tree().process_frame;mb(Vector2(840,897),false);await wait_s(.083)
  if int(counter.text)>0:accepted=true;break
 if not require(accepted,'real metronome accepted a strike only after warmup'):return
 var sample_current=named('SampleReceipt');if not require(sample_current!=null and sample_current.is_processing(),'accepted sample energises plate-to-marker trace'):return
 await shot('03-calibration-sample');var lit:=get_viewport().get_texture().get_image();await wait_s(.58);await RenderingServer.frame_post_draw
 var changed:=pixel_diffs(lit,Rect2i(300,932,1480,70),Vector2i.ZERO,get_viewport().get_texture().get_image());trail.append({'meterCurrentChangedSamples':changed});if not require(changed>12 and not sample_current.is_processing(),'visible current crosses sample bus, then settles'):return
 await click_id('CalibrationStart');await wait_s(.4)
 if not require(int(counter.text)==0,'stop resets samples without applying offset'):return
 await click_id('CalibrationBack');if not await active_page('settings'):return
func run_tour():
 if not await active_page('main'):return
 match suite:
  'palette':await palette()
  'library':await library()
  'store':await store()
  'calibration':await calibration()
  _:require(false,'unknown suite');return
 if failed:return
 report(true);print('MIGRATION QA PASS '+suite);await wait_s(.28);get_tree().quit(0)

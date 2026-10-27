// 音符主材质：主体与 design/note-light-v1/parity/material.gdshader 同一份实现
// （WebGL1 与 Godot 逐像素对照通过，见 design/note-light-v1/parity/REPORT.md）；
// 唯一差异是 u_mode=1 的命中残影分支——客户端新增，对照测试只用 u_mode=0。
//
// 约定：
// - 网格为「尺寸 + 2×u_pad」的矩形，UV 0..1；u_size 是音符本体尺寸（像素）。
// - 侧轨不旋转节点，而是把 MeshInstance2D 子节点旋转 90°，局部空间始终横排，
//   因此本 shader 不需要 vertical 分支。
// - 遮罩是数据纹理（NoteMask.cs 内嵌 PNG 解码），R=体面明暗，G=发光核心，B=边缘。
// - 颜色为单个 u_color，不再有旧材质的多段渐变与描边。
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
uniform float u_pad = 64.;
uniform sampler2D u_mask : filter_linear, repeat_disable;
uniform vec2 u_size;
uniform vec3 u_color;
uniform float u_energy;
uniform float u_glow;
uniform float u_kind;
uniform float u_opacity;
uniform float u_mode;
uniform float u_gold;
uniform float u_gold_width;
float line(vec2 p,vec2 a,vec2 b){vec2 v=b-a;return length(p-a-v*clamp(dot(p-a,v)/dot(v,v),0.,1.));}
float mark(vec2 p){float d=999.;
 if(u_kind>2.5){d=min(line(p,vec2(-5.5,-5.5),vec2(5.5,5.5)),line(p,vec2(-5.5,5.5),vec2(5.5,-5.5)));}
 else if(u_kind>1.5){
 d=min(line(p,vec2(-11.,-6.),vec2(-16.,0.)),line(p,vec2(-16.,0.),vec2(-11.,6.)));
 d=min(d,min(line(p,vec2(11.,-6.),vec2(16.,0.)),line(p,vec2(16.,0.),vec2(11.,6.))));
 d=min(d,abs(abs(p.x)+abs(p.y)-4.)*.7071);
 }return 1.-smoothstep(.8,1.7,d);}
void fragment(){
 vec4 item_modulate=COLOR;
 vec2 p=(UV-vec2(.5))*(u_size+vec2(u_pad*2.)),halfsize=u_size*.5,ap=abs(p);
 float edge=min(halfsize.x-ap.x,halfsize.y-ap.y);
 float cut=min(6.5,min(halfsize.x*.3,halfsize.y*.65));
 edge=min(edge,(halfsize.x+halfsize.y-cut-ap.x-ap.y)*.70710678);
 float silhouette=smoothstep(-.65,.65,edge);
 // Nine-slice horizontal mapping: fixed cap pixels, stretch only the clean center.
 float cap=min(12.,halfsize.x*.75);float mx=ap.x;
 float tx=mx>halfsize.x-cap ? .5+(116.+(mx-halfsize.x+cap)/cap*12.)/256.
 : .5+(mx/max(halfsize.x-cap,.01))*116./256.;
 vec2 uv=vec2(tx,clamp(p.y/u_size.y+.5,0.,1.));vec4 tex=texture(u_mask,uv);
 float core=tex.g;float energy=max(u_energy,0.);
 float hot=clamp(.20+energy*.37,0.,.95);
 vec3 base=u_color*(.42+.47*tex.r);
 base=mix(base,mix(u_color,vec3(1.),hot),core*(.56+energy*.17));
 base+=u_color*tex.b*.12;
 base=mix(base,mix(u_color,vec3(1.),.65),tex.b*.50);
 // Mine stays inset. Mixer is composited separately after the body, not clipped to it.
 if(u_kind>2.5){float slot=1.-smoothstep(17.,19.,abs(p.x)+abs(p.y)*1.3);
 base=mix(base,u_color*.12,slot*.91);
 base=mix(base,mix(u_color,vec3(1.),.7),mark(p));}
 if(u_kind>.5&&u_kind<1.5){
 float endmark=(1.-smoothstep(.8,1.9,abs(ap.x-(halfsize.x-10.))))*(1.-smoothstep(u_size.y*.22,u_size.y*.35,ap.y));
 base=mix(base,vec3(.94,.99,1.),endmark);}
 base=mix(base,vec3(1.),max(energy-1.35,0.)*.24*core);
 float outside=max(-edge,0.);float spread=5.5+energy*5.5;
 float nearGlow=exp(-outside*outside/(spread*spread));
 float farGlow=exp(-outside*outside/(spread*spread*6.));
 float halo=(nearGlow*.25+farGlow*.075)*(.48+energy*.65)*u_glow;
 // No black baked rectangle. Halo is calculated outside the exact silhouette.
 float alpha=clamp(halo,0.,.8)*(1.-silhouette)+silhouette;
 vec3 haloColor=mix(u_color,vec3(1.),clamp((energy-1.)*.12,0.,.18));
 vec3 result=base*silhouette+haloColor*clamp(halo,0.,.8)*(1.-silhouette);
 if(u_kind>1.5&&u_kind<2.5){
  // Independent 38 px center badge on a 24 px body. Scale with thickness, never width.
  float badgeScale=u_size.y/24.;vec2 bp=p/badgeScale;
  float badgeDistance=(abs(bp.x)+abs(bp.y)-19.)*.70710678*badgeScale;
  float badgeShape=1.-smoothstep(-.65,.65,badgeDistance);
  float rim=1.-smoothstep(.6,1.8,abs(badgeDistance));
  float innerMark=mark(bp);
  vec3 badgeColor=mix(u_color*.12,mix(u_color,vec3(1.),.64),max(rim,innerMark));
  float badgeOutside=max(badgeDistance,0.);
  float badgeGlow=exp(-badgeOutside*badgeOutside/(spread*spread*.65))*(.10+.10*energy)*u_glow*(1.-badgeShape);
  float badgeAlpha=badgeShape+badgeGlow*(1.-badgeShape);
  vec3 badgeResult=badgeColor*badgeShape+haloColor*badgeGlow*(1.-badgeShape);
  result=badgeResult+result*(1.-badgeAlpha);alpha=badgeAlpha+alpha*(1.-badgeAlpha);
 }
 // Sync accent: an independent outer ring, never a gold tint over the blue body.
 // 1 px dark separation + 3 px gold ring at the default width; no hit-driven whitening.
 if(u_gold>.5){
  float d=-edge,gap=1.;float width=u_gold_width;
  float outer=gap+width;
  vec3 goldColor=mix(vec3(1.,.67,.13),vec3(1.,.90,.47),1.-smoothstep(gap,outer,d));
  float ring=smoothstep(gap-.5,gap+.5,d)*(1.-smoothstep(outer-.5,outer+.5,d));
  float separation=(1.-silhouette)*(1.-smoothstep(gap-.25,gap+.35,d));
  result=mix(result,vec3(.025,.045,.075),separation*.92);
  alpha=separation*.92+alpha*(1.-separation*.92);
  float goldDistance=max(d-outer,0.);
  float goldHalo=exp(-goldDistance*goldDistance/49.)*.13*u_glow*smoothstep(outer-.5,outer+.7,d);
  vec3 warm=vec3(1.,.73,.24);
  result=warm*goldHalo+result*(1.-goldHalo);alpha=goldHalo+alpha*(1.-goldHalo);
  result=goldColor*ring+result*(1.-ring);alpha=ring+alpha*(1.-ring);
 }
 if(u_mode>.5){
  // 命中残影（仅客户端使用；u_mode=0 的对照路径逐像素不变）。
  // 不再拿本体剪影做平板覆盖，而是以音符中心为原点的椭圆径向衰减：
  // 中心白热、外围回到音符色；输出保持 result > alpha（过亮 premultiplied），
  // 合成时是在背景上「加光」，而不是盖一层发闷的半透明颜色。
  vec2 q=vec2(p.x/max(halfsize.x*.95,12.),p.y/max(halfsize.y*2.6,18.));
  float r2=dot(q,q);
  float hotCore=exp(-r2*3.4);
  float softGlow=exp(-r2*.55)*.55;
  float flashAlpha=clamp(hotCore+softGlow,0.,1.);
  float whiteMix=clamp(.22+hotCore*.52+max(energy-1.,0.)*.10,0.,.92);
  result=mix(u_color,vec3(1.),whiteMix)*flashAlpha;
  alpha=flashAlpha*.55;
}
 COLOR=vec4(result*u_opacity*item_modulate.rgb*item_modulate.a,alpha*u_opacity*item_modulate.a);
}
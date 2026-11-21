shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;
// Sample only this panel from the native canvas copy. Coordinates below include viewport scaling.
uniform sampler2D surface : hint_screen_texture, filter_linear, repeat_disable;
uniform vec2 pane = vec2(1280.0, 736.0);
uniform vec3 lens = vec3(5400.0,726.0,373.0); // radius, eye, half arc
uniform float ink_width = 1268.0;
uniform vec4 pulse0 = vec4(0.,0.,10.,0.);
uniform vec4 pulse1 = vec4(0.,0.,10.,0.);
uniform vec4 protect_rect = vec4(0.); // optional true cover inside the expanded library card
uniform vec3 wave_clip = vec3(-999.,-100.,0.); // visible source-card y min/max, reduced mode
uniform vec4 gutter : source_color = vec4(.051,.068,.098,1.);
vec3 packet(vec2 q, vec4 s){
 vec2 d=(q-s.xy)*vec2(1.,1.12);float r=length(d),f=(r-270.*s.z)/42.;
 float env=(1.-smoothstep(.15,1.,abs(f)))*smoothstep(0.,.035,s.z)*(1.-smoothstep(.61,.86,s.z))/(1.+r*.007);
 vec2 v=d/max(12.,r);float e=s.w*env;return vec3(e*cos(4.8*f),e*sin(4.8*f)*(vec2(.70,-.37)+.4*v));
}
void fragment(){
 vec2 p=UV*pane;float r=lens.x,eye=lens.y,sy=p.y-.5*pane.y;
 float a=asin(clamp(sy*(eye+r)/(r*length(vec2(eye,sy))),-.99999,.99999))-atan(sy,eye);
 float k=eye/(eye+r*(1.-cos(a)));
 vec2 q=vec2(p.x/k, (.5+.5*r*a/lens.z)*pane.y);
 vec3 w=vec3(0.);if(q.y>=wave_clip.x && q.y<=wave_clip.y && q.x<ink_width){w=packet(q,pulse0)+packet(q,pulse1);}
 if(wave_clip.z>.5){
     float r0=length(q-pulse0.xy);float r1=length(q-pulse1.xy);
     float lamp=max(exp(-r0*.037)*(1.-smoothstep(0.,.14,pulse0.z))*abs(pulse0.w),
                    exp(-r1*.037)*(1.-smoothstep(0.,.14,pulse1.z))*abs(pulse1.w));
     w=vec3(.52*lamp,0.,0.);
 }
 if(wave_clip.y-wave_clip.x>160.){
   // Preserve legibility of the title/author/difficulty; copper beneath still flexes.
   float safe_type=smoothstep(325.,358.,q.x)*(1.-smoothstep(229.,249.,q.y-wave_clip.x));
   vec2 a=q-protect_rect.xy;vec2 b=protect_rect.zw-a;
   float safe_cover=step(1.,protect_rect.z)*smoothstep(-2.,2.,min(min(a.x,a.y),min(b.x,b.y)));
   w*=1.-max(.83*safe_type,.96*safe_cover);
 }
 vec2 dq=w.yz*3.3;
 vec2 lookup=clamp(q+dq,vec2(.52),vec2(max(.52,ink_width-.52),pane.y-.52));
 // Derivatives express one native design pixel in the current render target's UV system,
 // including black bars, viewport scaling and orientation, independent of texture dimensions.
 vec2 screen_vec_x=dFdx(SCREEN_UV)/dFdx(p.x);
 vec2 screen_vec_y=dFdy(SCREEN_UV)/dFdy(p.y);
 vec2 st=SCREEN_UV+(lookup.x-p.x)*screen_vec_x+(lookup.y-p.y)*screen_vec_y;
 vec4 ink=texture(surface,st);float facing=1.-.42*(1.-cos(a))-.05*max(sin(a),0.);
 vec3 layer=ink.rgb*facing*(1.-.06*max(0.,-w.x))+vec3(.07,.045,.105)*max(0.,w.x);
 vec3 result=mix(gutter.rgb,layer,1.-smoothstep(ink_width-.6,ink_width+.6,q.x));
 if(p.x>=ink_width){result=texture(surface,SCREEN_UV).rgb;} // unwarped native scrollbar lane
 COLOR=vec4(result,1.0);
}

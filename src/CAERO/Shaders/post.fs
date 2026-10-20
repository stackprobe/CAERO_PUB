#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform sampler2D blurredScene;
uniform vec2 resolution;
uniform sampler2D sceneDepth;
uniform vec3 rayHeight;
uniform float waterPass,eyeHeight,waterLevel,nearClip,farClip;
out vec4 finalColor;
vec4 sceneSample(vec2 uv) {
    vec4 encoded=texture(texture0,uv);
    return vec4(encoded.rgb*encoded.rgb*2.0,encoded.a);
}
vec3 sceneColor(vec2 uv) { return sceneSample(uv).rgb; }
void main() {
    vec2 uv=fragTexCoord;
    vec2 pixel=1.0/resolution;
    vec4 centre=sceneSample(uv);
    vec3 c=centre.rgb;
    vec3 filteredColor=c;
    float submerged=0.0;
    if(waterPass>.5) {
        // A single depth read classifies this visible ray, not blur strength.
        // Start at the near plane: it can straddle the surface while the eye is above it.
        float z=texture(sceneDepth,uv).r*2.0-1.0;
        float depth=2.0*nearClip*farClip/(farClip+nearClip-z*(farClip-nearClip));
        vec2 screen=uv*2.0-1.0;
        float rayY=rayHeight.x+screen.x*rayHeight.y+screen.y*rayHeight.z;
        float nearY=eyeHeight+nearClip*rayY, targetY=eyeHeight+depth*rayY;
        // Only the reconstructed surface hit needs a tolerance for depth quantization.
        submerged=(nearY<waterLevel || targetY<waterLevel-.002)?1.0:0.0;
    }
    if(submerged>0.0) {
        vec3 filtered=texture(blurredScene,uv).rgb;
        filteredColor=filtered*filtered*2.0;
        // Preserve the depth-tested eye itself, with only its halo blurred.
        float eyeMask=1.0-smoothstep(.02,.12,abs(centre.a-.5));
        c=mix(filteredColor,centre.rgb,eyeMask);
    }
    // A small FXAA filter smooths silhouettes rendered in the offscreen buffer.
    vec4 sampleNW=sceneSample(uv+vec2(-1,-1)*pixel);
    vec4 sampleNE=sceneSample(uv+vec2(1,-1)*pixel);
    vec4 sampleSW=sceneSample(uv+vec2(-1,1)*pixel);
    vec4 sampleSE=sceneSample(uv+vec2(1,1)*pixel);
    vec3 nw=sampleNW.rgb, ne=sampleNE.rgb, sw=sampleSW.rgb, se=sampleSE.rgb;
    vec3 luma=vec3(.299,.587,.114);
    float lnw=dot(nw,luma), lne=dot(ne,luma), lsw=dot(sw,luma), lse=dot(se,luma), lc=dot(c,luma);
    float lmin=min(lc,min(min(lnw,lne),min(lsw,lse)));
    float lmax=max(lc,max(max(lnw,lne),max(lsw,lse)));
    if(submerged<=0.0 && lmax-lmin > max(.025,lmax*.10)) {
        vec2 dir=vec2(-((lnw+lne)-(lsw+lse)),(lnw+lsw)-(lne+lse));
        float reduction=max((lnw+lne+lsw+lse)*.03125,.0078125);
        dir=clamp(dir/(min(abs(dir.x),abs(dir.y))+reduction),vec2(-6),vec2(6))*pixel;
        vec3 a=.5*(sceneColor(uv-dir/6.0)+sceneColor(uv+dir/6.0));
        vec3 b=a*.5+.25*(sceneColor(uv-dir*.5)+sceneColor(uv+dir*.5));
        float lb=dot(b,luma); c=(lb<lmin || lb>lmax)?a:b;
    }
    // Reuse the five existing colour reads: a tiny, fish-only edge correction.
    // Alpha=0 marks body fragments, .5 emissive eyes, and 1 scenery.
    float maskMin=min(centre.a,min(min(sampleNW.a,sampleNE.a),min(sampleSW.a,sampleSE.a)));
    float maskMax=max(centre.a,max(max(sampleNW.a,sampleNE.a),max(sampleSW.a,sampleSE.a)));
    float fishEdge=maskMax-maskMin;
    float peak=max(dot(centre.rgb,luma),max(max(lnw,lne),max(lsw,lse)));
    float keepEyes=1.0-smoothstep(.14,.4,peak);
    c=max(c+((nw+ne+sw+se)*.25-centre.rgb)*(.14*fishEdge*keepEyes*(1.0-submerged)),vec3(0));
    if(submerged>.5) {
        // A continuous halo, never eight shifted copies of a tiny eye.
        c+=max(filteredColor-vec3(.16),vec3(0))*.96;
    } else {
        vec3 bloom=vec3(0);
        for(int i=0;i<8;i++) {
            float a=float(i)*.785398;
            vec3 tap=sceneColor(uv+vec2(cos(a),sin(a))*3.0/resolution);
            bloom+=max(tap-vec3(.78),vec3(0));
        }
        c+=bloom*.03;
    }
    c*=1.08;
    // Filmic rolloff, then display transfer; lighting above is in linear space.
    c=clamp((c*(2.51*c+.03))/(c*(2.43*c+.59)+.14),0.0,1.0);
    c=pow(c,vec3(1.0/2.2));
    c=mix(vec3(dot(c,vec3(.299,.587,.114))),c,.97);
    float vignette=1.0-.19*pow(length((uv-.5)*vec2(1.1,1)),2.0);
    float dither=fract(sin(dot(gl_FragCoord.xy,vec2(12.9898,78.233)))*43758.5453)-.5;
    finalColor=vec4(clamp(c*vignette+dither/255.0,0.0,1.0),1);
}

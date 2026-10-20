#version 330
in vec3 worldPosition;
in vec3 materialPosition;
in vec3 normal;
in vec4 tint;
in vec2 materialUV;
out vec4 finalColor;
uniform sampler2D texture0;
uniform sampler2D waterNoise;
uniform sampler2D surfaceDetail;
uniform vec3 eye;
uniform vec3 materialOffset, waterPhase;
uniform float night;
uniform vec3 sunDirection,moonDirection;
uniform float sunOpacity,moonOpacity;
uniform float time;
uniform float drawDistance;
uniform float waterLevel;
uniform float waterBrightness;
uniform float waterDensity;
uniform vec3 waterFogColor;
uniform int seabedPass;
uniform float characterPass;
uniform float fishPass, fishEye;
uniform vec3 wallFinish; // island wall style, block scale, mineral tint
uniform float wallStrength;
uniform float primeNumber;
uniform vec4 colDiffuse;
uniform int lightCount;
uniform vec4 lamps[24]; // xyz position, w radius
uniform float lampIntensity[24];
uniform vec3 lampMin[24];
uniform vec3 lampMax[24];
vec3 skyColor(vec3 ray) {
    vec3 horizon=mix(vec3(.48,.56,.60),vec3(.0008,.0014,.0025),night);
    vec3 zenith=mix(vec3(.24,.36,.48),vec3(.0002,.0005,.0012),night);
    float twilight=1.0-smoothstep(0.0,.22,abs(sunDirection.y));
    float towardSun=pow(max(dot(normalize(ray.xz+vec2(.00001)),normalize(sunDirection.xz)),0.0),4.0);
    horizon=mix(horizon,vec3(.55,.22,.11),twilight*towardSun*.55);
    return mix(horizon,zenith,pow(max(ray.y,0.0),.45)*.85);
}
float masonry(vec2 uv,vec2 size,float stagger) {
    float row=floor(uv.y/size.y);
    vec2 tile=fract(vec2(uv.x/size.x+mod(row,2.0)*stagger,uv.y/size.y));
    float edge=min(min(tile.x,1.0-tile.x),min(tile.y,1.0-tile.y));
    return mix(.46,1.0,smoothstep(.015,.055,edge));
}
void main() {
    vec3 p=worldPosition, n=normalize(normal), view=normalize(eye-p);
    float distanceToEye=length(p-eye);
    float nearVision=1.0-smoothstep(5.0,24.0,distanceToEye);
    float density=mix(.0045,mix(.068,.025,nearVision),night)*(.78+.22*exp(-max(p.y,0.0)*.025));
    float fog=1.0-exp(-distanceToEye*density);
    fog+=.035*(1.0-exp(-distanceToEye*.004))*exp(-max(p.y+18.0,0.0)*.055);
    fog=mix(fog,1.0,smoothstep(drawDistance*.75,drawDistance*.96,distanceToEye));
    vec3 fogColor=skyColor(normalize(p-eye));
    // Large chunks can cross the cutoff. Hidden air fragments need no textures,
    // material and lighting reads. Keep the separate underwater optics.
    if(fog>=1.0 && min(eye.y,p.y)>=waterLevel) {
        finalColor=vec4(sqrt(clamp(fogColor*.5,0.0,1.0)),1);
        return;
    }
    vec3 lit;
    if(seabedPass != 0) {
        lit=vec3(.001,.0015,.002);
    } else if(tint.a*255.0<120.0) {
        float wx=p.x*.31+p.z*.17+waterPhase.x+time*.4;
        float wz=p.z*.26+waterPhase.y-time*.33+sin(p.x*.09+waterPhase.z);
        float waveLod=1.0-smoothstep(35.0,190.0,distanceToEye);
        n=normalize(vec3(-.025*cos(wx)*waveLod,1,-.017*cos(wz)*waveLod));
        vec3 reflection=reflect(-view,n);
        float fresnel=.08+.83*pow(1.0-max(dot(view,n),0.0),4.0);
        vec3 deep=p.y<0?vec3(.026,.105,.118):vec3(.025,.065,.043);
        float waterDetail=texture(waterNoise,p.xz/24.0+vec2(time*.006,-time*.004)).r;
        deep*=.94+.12*waterDetail;
        if(eye.y < waterLevel && p.y <= waterLevel+.01) {
            // The underside sees water, rather than reflecting the bright above-water sky.
            float facing=clamp(dot(view,-n),0.0,1.0);
            lit=mix(vec3(.045,.10,.12),vec3(.12,.22,.23),facing);
            lit*=.94+.12*waterDetail;
        } else {
            lit=mix(deep,skyColor(reflection),fresnel);
        }
    } else if(characterPass > .5 || fishPass > .5) {
        // Cloth and skin retain the same fog and lantern lighting as the world,
        // without architecture's stone grain, paving joints or damp coloration.
        vec3 ambient=mix(vec3(.48,.50,.52),vec3(.66,.69,.72),n.y*.5+.5);
        lit=pow(tint.rgb*colDiffuse.rgb,vec3(2.0))*.84*ambient;
        if(fishPass>.5) lit*=.06;
    } else {
        // Shared, world-space materials allow new islands without baking a global atlas.
        float tag=tint.a*255.0;
        vec3 baseTint=tint.rgb;
        if(primeNumber>.5) {
            if(abs(tag-251.0)<.5) baseTint=vec3(.97,.965,.94);
            if(abs(tag-252.0)<.5) baseTint=vec3(.78,.045,.065);
        }
        lit=pow(baseTint,vec3(2.0))*.84;
        vec3 material=materialPosition+materialOffset;
        float weather=texture(surfaceDetail,material.xz/37.0).g;
        lit*=.78+.30*weather;
        float damp=(1.0-smoothstep(waterLevel,waterLevel+14.0,p.y))*(.25+.25*weather);
        lit=mix(lit,lit*vec3(.55,.72,.59),damp);
        bool inlaid=tag>250.5 && tag<253.5;
        bool rock=tag<165.0, foliage=tag>=165.0 && tag<200.0;
        bool carved=tag>=200.0 && tag<229.0, paving=tag>=229.0 && tag<245.0;
        bool concrete=tag>=245.0 && tag<254.0 && !inlaid;
        vec2 q=abs(n.y)>.7?material.xz:(abs(n.x)>.7?material.zy:material.xy);
        vec4 detail=texture(surfaceDetail,q/(foliage?.65:(rock?4.0:2.4)));
        float grain=foliage?detail.b:(rock?detail.g:detail.r);
        lit*=1.0+(grain-.5)*(inlaid?.12:(foliage?.30:(rock?.65:.75)));
        if(concrete) {
            vec2 panel=abs(fract(q/vec2(6.0,3.0))-.5)*vec2(6.0,3.0);
            float edge=max(smoothstep(2.975,3.0,panel.x),smoothstep(1.475,1.5,panel.y));
            float fade=1.0-smoothstep(40.0,130.0,distanceToEye);
            lit*=1.0-edge*.22*fade;
            vec2 pin=abs(mod(q+vec2(.3),vec2(6.0,3.0))-.6);
            lit*=1.0-(1.0-smoothstep(.055,.09,length(pin)))*.25*fade;
        }
        if(!foliage && !rock && !carved && !concrete && !inlaid) {
            vec2 blockSize=paving?vec2(2.4,2.4):vec2(2.8,1.2);
            lit*=texture(surfaceDetail,q/(blockSize*2.0)).a;
        }
        if(wallFinish.x>.5 && abs(n.y)<.7 && tag>=200.0 && tag<254.0 && !inlaid) {
            vec2 wall=q/wallFinish.y;
            float pattern=1.0;
            vec3 mineral=vec3(1);
            float coarse=texture(surfaceDetail,wall/8.0).g;
            float fine=texture(surfaceDetail,wall/1.3).r;
            if(wallFinish.x<1.5) {
                pattern=masonry(wall,vec2(1.65,.65),.5); mineral=vec3(1.12,.72,.55); // red brick
            } else if(wallFinish.x<2.5) {
                pattern=.65+.35*smoothstep(-.7,.3,sin(wall.y*7.0+coarse*5.0)); mineral=vec3(.67,.83,.92); // strata
            } else if(wallFinish.x<3.5) {
                pattern=masonry(wall,vec2(5,2.5),0); mineral=vec3(.82,.85,.79); // concrete panels
            } else if(wallFinish.x<4.5) {
                pattern=masonry(wall,vec2(3.8,1.6),.5); mineral=vec3(1.12,1.02,.77); // limestone ashlar
            } else if(wallFinish.x<5.5) {
                pattern=.55+.45*smoothstep(.15,.7,fine); mineral=vec3(.51,.58,.63); // dark basalt
            } else if(wallFinish.x<6.5) {
                pattern=.68+.32*smoothstep(-.4,.6,sin(wall.y*12.0+coarse*3.0)); mineral=vec3(.68,.73,.79); // slate
            } else if(wallFinish.x<7.5) {
                pattern=1.0-.48*pow(.5+.5*sin(wall.x*1.8+wall.y*.9+coarse*10.0),12.0); mineral=vec3(1.17,1.14,1.1); // marble veins
            } else if(wallFinish.x<8.5) {
                pattern=.65+.35*smoothstep(.08,.5,fine*coarse); mineral=vec3(1.1,.91,.65); // porous sandstone
            } else if(wallFinish.x<9.5) {
                pattern=masonry(wall,vec2(.45,.45),0); mineral=vec3(.72,1.02,.99); // glazed tiles
            } else if(wallFinish.x<10.5) {
                pattern=.72+.28*step(.5,fract(wall.x*2.0)); mineral=vec3(.85,.73,.58); // fluted stone
            } else if(wallFinish.x<11.5) {
                pattern=masonry(wall,vec2(1.8,.32),.5); mineral=vec3(.69,.54,.48); // long roman brick
            } else if(wallFinish.x<12.5) {
                pattern=masonry(wall,vec2(1.8,1.8),.5)*(.72+.28*coarse); mineral=vec3(.76,.88,.67); // mossy blocks
            } else if(wallFinish.x<13.5) {
                vec2 diagonal=vec2(wall.x+wall.y,wall.x-wall.y);
                pattern=masonry(diagonal,vec2(1.4,1.4),0); mineral=vec3(1.04,.9,.78); // diamond tiles
            } else if(wallFinish.x<14.5) {
                pattern=1.0-.45*pow(1.0-abs(sin(wall.y*2.5+coarse*5.0)),8.0); mineral=vec3(.95,.97,.91); // cracked plaster
            } else if(wallFinish.x<15.5) {
                pattern=.6+.4*smoothstep(.2,.75,coarse+fine*.25); mineral=vec3(.65,.82,.76); // oxidized copper
            } else if(wallFinish.x<16.5) {
                pattern=.5+.5*smoothstep(.2,.55,texture(surfaceDetail,wall/vec2(.3,6)).r); mineral=vec3(.85,.65,.46); // timber grain
            } else if(wallFinish.x<17.5) {
                vec2 rib=abs(fract(wall/vec2(.85,2.8))-.5);
                pattern=.65+.35*step(.08,min(rib.x,rib.y)); mineral=vec3(.73,.78,.84); // ribbed metal
            } else if(wallFinish.x<18.5) {
                pattern=masonry(wall+vec2(coarse*.5,0),vec2(2.2,1.15),.5); mineral=vec3(.89,.78,.7); // rough rubble
            } else if(wallFinish.x<19.5) {
                vec2 dotCell=fract(wall/.6)-.5;
                pattern=1.0-.55*(1.0-smoothstep(.12,.2,length(dotCell))); mineral=vec3(.81,.86,.88); // perforated panels
            } else {
                pattern=.58+.42*step(.5,fract((wall.x+abs(fract(wall.y)-.5)*2.0)*2.0)); mineral=vec3(.9,.78,.58); // chevrons
            }
            vec3 finish=pattern*mineral*mix(vec3(.86,.92,.82),vec3(1.1,.94,.87),wallFinish.z);
            lit*=mix(vec3(1),finish,wallStrength);
        }
        // Broad skylight changes smoothly with time; no cast or contact shadows.
        vec3 ambient=mix(vec3(.48,.50,.52),vec3(.66,.69,.72),n.y*.5+.5);
        lit*=ambient;
    }
    // Neutral, omnidirectional adaptation: no cone, flame tint or moving light.
    vec3 surface=lit;
    lit*=mix(1.0,.035+.24*nearVision,night);
    if(seabedPass==0) {
        for(int i=0;i<lightCount;i++) {
            if(lampIntensity[i]<=0.0) continue;
            if(any(lessThan(p,lampMin[i])) || any(greaterThan(p,lampMax[i]))) continue;
            vec3 delta=lamps[i].xyz-p;
            float d=length(delta);
            float fade=max(1.0-d/lamps[i].w,0.0);
            float diffuse=.22+.78*max(dot(n,delta/max(d,.001)),0.0);
            lit+=surface*vec3(1.0,.65,.30)*fade*fade*diffuse*1.8*lampIntensity[i];
        }
    }
    if(min(eye.y,p.y) < waterLevel) {
        float eyeDepth=max(waterLevel-eye.y,0.0);
        float targetDepth=max(waterLevel-p.y,0.0);
        // Only the submerged segment contributes water fog, including views from just above.
        float waterFraction=clamp((eyeDepth+targetDepth)/max(abs(p.y-eye.y),.0001),0.0,1.0);
        float waterDistance=distanceToEye*waterFraction;
        float extraPathDepth=max((targetDepth-eyeDepth)*.5,0.0);
        // Looking down from the surface still traverses deep, dark water.
        fogColor=waterFogColor*exp(-.18*extraPathDepth);
        fog=1.0-exp(-waterDistance*(waterDensity+.016*extraPathDepth));
        float objectBrightness=waterBrightness*exp(-.18*max(targetDepth-eyeDepth,0.0));
        lit*=vec3(.55,.80,.85)*objectBrightness;
    }
    // Square-root encoding preserves subtle material gradients in an RGBA8 target.
    vec3 result=mix(lit,fogColor,clamp(fog,0.0,1.0));
    // Absorb the water's scattered light as well as surface illumination: the
    // body should read as a faint dark silhouette, rather than a pale fog shape.
    if(fishPass>.5 && fishEye<.5)
        result*=1.0-.78*exp(-distanceToEye*.035);
    // Self-emission bypasses daylight/depth dimming. Distance softens the eyes;
    // depth testing still prevents their light from showing through solid walls.
    if(fishEye>.5 && eye.y<waterLevel)
        result+=vec3(1.15,1.5,.5)*exp(-distanceToEye*.035);
    // Opaque fish store metadata in alpha: body=0, emissive eye=.5, scenery=1.
    float sceneAlpha=fishPass>.5?(fishEye>.5?.5:0.0):1.0;
    finalColor=vec4(sqrt(clamp(result*.5,0.0,1.0)),sceneAlpha);
}

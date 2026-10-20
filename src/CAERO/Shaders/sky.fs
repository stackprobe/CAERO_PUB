#version 330
out vec4 finalColor;
uniform vec2 resolution;
uniform vec3 forward,right,up,eye;
uniform float tanHalfFov,waterLevel,seaFloorLevel,nearClip;
uniform vec3 waterFogColor;
uniform float night;
uniform vec3 sunDirection,moonDirection;
uniform float sunOpacity,moonOpacity;
vec3 celestial(vec3 ray) {
    float sunDot=dot(ray,sunDirection), moonDot=dot(ray,moonDirection);
    float sunDisc=smoothstep(cos(.016),cos(.014),sunDot);
    float moonDisc=smoothstep(cos(.014),cos(.012),moonDot);
    float glow=pow(max(sunDot,0.0),400.0)*.22;
    vec3 moonRight=normalize(cross(moonDirection,vec3(0,1,0)));
    vec3 moonUp=cross(moonRight,moonDirection);
    vec2 uv=vec2(dot(ray,moonRight),dot(ray,moonUp))/.013;
    float craters=.82+.10*sin(uv.x*14.0+sin(uv.y*9.0))*.5+.08*sin(uv.y*21.0+uv.x*5.0);
    vec3 sunColor=mix(vec3(3.2,1.0,.28),vec3(3.2,2.7,1.8),smoothstep(0.0,.22,sunDirection.y));
    return (sunColor*sunDisc+vec3(1.0,.46,.12)*glow)*sunOpacity
        +vec3(1.1,1.22,1.4)*moonDisc*craters*moonOpacity;
}
void main() {
    vec2 screen=gl_FragCoord.xy/resolution*2.0-1.0;
    vec3 ray=normalize(forward+right*screen.x*(resolution.x/resolution.y)*tanHalfFov+up*screen.y*tanHalfFov);
    float elevation=pow(max(ray.y,0.0),.45);
    vec3 horizon=mix(vec3(.48,.56,.60),vec3(.0008,.0014,.0025),night);
    vec3 zenith=mix(vec3(.24,.36,.48),vec3(.0002,.0005,.0012),night);
    float twilight=1.0-smoothstep(0.0,.22,abs(sunDirection.y));
    float towardSun=pow(max(dot(normalize(ray.xz+vec2(.00001)),normalize(sunDirection.xz)),0.0),4.0);
    horizon=mix(horizon,vec3(.55,.22,.11),twilight*towardSun*.55);
    vec3 sky=mix(horizon,zenith,elevation*.85);
    // Soft cloud veils behind the moving, horizon-faded celestial discs.
    vec2 q=ray.xz/(max(ray.y,0.0)+.38)*vec2(2.0,.7);
    float band1=exp(-pow((q.y+1.5+.12*sin(q.x*1.7))/.40,2.0));
    float band2=exp(-pow((q.y+.15+.08*sin(q.x*2.0+1.0))/.30,2.0));
    float veil=(band1*.55+band2*.35)*(.65+.35*sin(q.x*1.5))*smoothstep(.02,.20,ray.y);
    sky=mix(sky,mix(vec3(.56,.61,.64),vec3(.001,.0016,.0026),night),veil*.38);
    sky+=celestial(ray)*smoothstep(-.005,.015,ray.y);
    float nearY=eye.y+ray.y*nearClip/max(dot(ray,forward),.001);
    if(nearY<waterLevel) {
        sky=waterFogColor;
        if(ray.y<0.0) sky*=exp(-.18*max((min(eye.y,waterLevel)-seaFloorLevel)*.5,0.0));
    }
    finalColor=vec4(sqrt(clamp(sky*.5,0.0,1.0)),1);
}

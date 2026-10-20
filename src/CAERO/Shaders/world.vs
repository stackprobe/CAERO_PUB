#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
uniform mat4 matModel;
uniform float fishWarp, fishTime, fishSpeed;
out vec3 worldPosition;
out vec3 materialPosition;
out vec3 normal;
out vec4 tint;
out vec2 materialUV;
void main() {
    vec3 position=vertexPosition;
    position.x+=fishWarp*sin(fishTime*fishSpeed*1.5+position.z*.38)*pow(clamp((2.0-position.z)/14.0,0.0,1.0),2.0)*1.0;
    worldPosition = (matModel * vec4(position, 1.0)).xyz;
    materialPosition = position;
    normal = normalize(mat3(matModel) * vertexNormal);
    tint = vertexColor;
    materialUV = vertexTexCoord;
    gl_Position = mvp * vec4(position, 1.0);
}

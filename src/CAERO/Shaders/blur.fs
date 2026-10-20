#version 330
in vec2 fragTexCoord;
uniform sampler2D texture0;
uniform vec2 stepUv;
out vec4 finalColor;
vec3 linearColor(vec2 uv) {
    vec3 encoded=texture(texture0,uv).rgb;
    return encoded*encoded*2.0;
}
void main() {
    if(all(equal(stepUv,vec2(0)))) {
        // Downsample first. Alpha is metadata, never opacity for this copy.
        finalColor=vec4(texture(texture0,fragTexCoord).rgb,1);
        return;
    }
    // Contiguous 25-tap Gaussian, sigma 5.5, paired into 13 bilinear reads.
    // Widening the kernel instead of spacing taps apart avoids duplicate point lights.
    const float offsets[6]=float[](1.487605845,3.471106606,5.454670261,7.438332106,9.422126590,11.406087032);
    const float weights[6]=float[](.142496908,.120952290,.090046807,.058798438,.033674659,.016915166);
    vec3 c=linearColor(fragTexCoord)*.074231465;
    for(int i=0;i<6;i++)
        c+=(linearColor(fragTexCoord+stepUv*offsets[i])
           +linearColor(fragTexCoord-stepUv*offsets[i]))*weights[i];
    // Keep the same encoding as the scene, and overwrite the entire target.
    finalColor=vec4(sqrt(max(c*.5,vec3(0))),1);
}

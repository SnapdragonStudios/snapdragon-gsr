#version 320 es

//============================================================================================================
//
//
//                  Copyright (c) 2024, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

precision mediump float;
precision highp int;


float FastLanczos(float base)
{
	float y = base - 1.0f;
	float y2 = y * y;
	float y_temp = 0.75f * y + y2;
	return y_temp * y2;
}

layout(location = 0) out mediump vec4 Output;
layout(location = 0) in highp vec2 texCoord;

layout(binding = 1) uniform mediump sampler2D PrevOutput;
layout(binding = 2) uniform mediump sampler2D MotionDepthClipAlphaBuffer;
layout(binding = 3) uniform mediump sampler2D InputColor;

/*
UBO description
    renderSize = {InputResolution.x, InputResolution.y}
    outputSize = {OutputResolution.x, OutputResolution.y}
    renderSizeRcp = {1.0 / InputResolution.x, 1.0 / InputResolution.y}
    outputSizeRcp = {1.0 / OutputResolution.x, 1.0 / OutputResolution.y}
    jitterOffset = {jitter.x, jitter.y},
    scaleRatio = {OutputResolution.x / InputResolution.x, min(20.0, pow((OutputResolution.x*OutputResolution.y) / (InputResolution.x*InputResolution.y), 3.0)},
    angleHor = tan(radians(m_Camera.verticalFOV / 2)) * InputResolution.x / InputResolution.y
    MinLerpContribution = sameCameraFrmNum? 0.3: 0.0;
    sameCameraFrmNum  //the frame number where camera pose is exactly same with previous frame
*/
layout(binding = 0) uniform readonly Params
{
    highp vec4                 clipToPrevClip[4];
    highp vec2                 renderSize;
    highp vec2                 outputSize;
    highp vec2                 renderSizeRcp;
    highp vec2                 outputSizeRcp;
    highp vec2                 jitterOffset;
    highp vec2                 scaleRatio;
    highp float                cameraFovAngleHor;
    highp float                minLerpContribution;
    highp float                reset;
    uint                       sameCameraFrmNum;
} params;

void main()
{
    float Biasmax_viewportXScale = params.scaleRatio.x;
    float scalefactor = params.scaleRatio.y;

    highp vec2 Hruv = texCoord;

    highp vec2 Jitteruv;
    Jitteruv.x = clamp(Hruv.x + (params.jitterOffset.x * params.outputSizeRcp.x), 0.0, 1.0);
    Jitteruv.y = clamp(Hruv.y + (params.jitterOffset.y * params.outputSizeRcp.y), 0.0, 1.0);

    ivec2 InputPos = ivec2(Jitteruv * params.renderSize);

    highp vec3 mda = textureLod(MotionDepthClipAlphaBuffer, Jitteruv, 0.0).xyz;
    highp vec2 Motion = mda.xy;

    highp vec2 PrevUV;
    PrevUV.x = clamp(-0.5 * Motion.x + Hruv.x, 0.0, 1.0);
    PrevUV.y = clamp(-0.5 * Motion.y + Hruv.y, 0.0, 1.0);

    float depthfactor = mda.z;

    vec3 HistoryColor = textureLod(PrevOutput, PrevUV, 0.0).xyz;

    /////upsample and compute box
    vec4 Upsampledcw = vec4(0.0);
    float biasmax = Biasmax_viewportXScale ;
    float biasmin = max(1.0f, 0.3 + 0.3 * biasmax);
    float biasfactor = 0.25f * depthfactor;
    float kernelbias = mix(biasmax, biasmin, biasfactor);
    float motion_viewport_len = length(Motion * params.outputSize);
    float curvebias = mix(-2.0, -3.0, clamp(motion_viewport_len * 0.02, 0.0, 1.0));

    vec3 rectboxcenter = vec3(0.0);
    vec3 rectboxvar = vec3(0.0);
    float rectboxweight = 0.0;
    highp vec2 srcpos = highp vec2(InputPos) + highp vec2(0.5) - params.jitterOffset;

    kernelbias *= 0.5f;
    float kernelbias2 = kernelbias * kernelbias;
    vec2 srcpos_srcOutputPos = srcpos - Hruv * params.renderSize;  //srcOutputPos = Hruv * params.renderSize;
    vec3 rectboxmin;
    vec3 rectboxmax;
    vec3 topMid = texelFetch(InputColor, InputPos + ivec2(0, 1), 0).xyz;
    {

        vec3 samplecolor = topMid;
        vec2 baseoffset = srcpos_srcOutputPos + vec2(0.0, 1.0);
        float baseoffset_dot = dot(baseoffset, baseoffset);
        float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
        float weight = FastLanczos(base);
        Upsampledcw += vec4(samplecolor * weight, weight);
        float boxweight = exp(baseoffset_dot * curvebias);
        rectboxmin = samplecolor;
        rectboxmax = samplecolor;
        vec3 wsample = samplecolor * boxweight;
        rectboxcenter += wsample;
        rectboxvar += (samplecolor * wsample);
        rectboxweight += boxweight;
    }
    vec3 rightMid = texelFetch(InputColor, InputPos + ivec2(1, 0), 0).xyz;
    {

        vec3 samplecolor = rightMid;
        vec2 baseoffset = srcpos_srcOutputPos + vec2(1.0, 0.0);
        float baseoffset_dot = dot(baseoffset, baseoffset);
        float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
        float weight = FastLanczos(base);
        Upsampledcw += vec4(samplecolor * weight, weight);
        float boxweight = exp(baseoffset_dot * curvebias);
        rectboxmin = min(rectboxmin, samplecolor);
        rectboxmax = max(rectboxmax, samplecolor);
        vec3 wsample = samplecolor * boxweight;
        rectboxcenter += wsample;
        rectboxvar += (samplecolor * wsample);
        rectboxweight += boxweight;
    }
    vec3 leftMid = texelFetch(InputColor, InputPos + ivec2(-1, 0) , 0).xyz;
    {

        vec3 samplecolor = leftMid;
        vec2 baseoffset = srcpos_srcOutputPos + vec2(-1.0, 0.0);
        float baseoffset_dot = dot(baseoffset, baseoffset);
        float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
        float weight = FastLanczos(base);
        Upsampledcw += vec4(samplecolor * weight, weight);
        float boxweight = exp(baseoffset_dot * curvebias);
        rectboxmin = min(rectboxmin, samplecolor);
        rectboxmax = max(rectboxmax, samplecolor);
        vec3 wsample = samplecolor * boxweight;
        rectboxcenter += wsample;
        rectboxvar += (samplecolor * wsample);
        rectboxweight += boxweight;
    }
    vec3 centerMid = texelFetch(InputColor, InputPos + ivec2(0, 0) , 0).xyz;
    {

        vec3 samplecolor = centerMid;
        vec2 baseoffset = srcpos_srcOutputPos;
        float baseoffset_dot = dot(baseoffset, baseoffset);
        float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
        float weight = FastLanczos(base);
        Upsampledcw += vec4(samplecolor * weight, weight);
        float boxweight = exp(baseoffset_dot * curvebias);
        rectboxmin = min(rectboxmin, samplecolor);
        rectboxmax = max(rectboxmax, samplecolor);
        vec3 wsample = samplecolor * boxweight;
        rectboxcenter += wsample;
        rectboxvar += (samplecolor * wsample);
        rectboxweight += boxweight;
    }
    vec3 btmMid = texelFetch(InputColor, InputPos + ivec2(0, -1) , 0).xyz;
    {

        vec3 samplecolor = btmMid;
        vec2 baseoffset = srcpos_srcOutputPos + vec2(0.0, -1.0);
        float baseoffset_dot = dot(baseoffset, baseoffset);
        float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
        float weight = FastLanczos(base);
        Upsampledcw += vec4(samplecolor * weight, weight);
        float boxweight = exp(baseoffset_dot * curvebias);
        rectboxmin = min(rectboxmin, samplecolor);
        rectboxmax = max(rectboxmax, samplecolor);
        vec3 wsample = samplecolor * boxweight;
        rectboxcenter += wsample;
        rectboxvar += (samplecolor * wsample);
        rectboxweight += boxweight;
    }

    //if (params.sameCameraFrmNum!=0u)  //maybe disable this for ultra performance
    if (false)  //maybe disable this for ultra performance, true could generate more realistic output
    {
        {
            vec3 topRight = texelFetch(InputColor, InputPos + ivec2(1, 1), 0).xyz;
            vec3 samplecolor = topRight;
            vec2 baseoffset = srcpos_srcOutputPos + vec2(1.0, 1.0);
            float baseoffset_dot = dot(baseoffset, baseoffset);
            float base = clamp(baseoffset_dot * kernelbias2, 0.0, 1.0);
            float weight = FastLanczos(base);
            Upsampledcw += vec4(samplecolor * weight, weight);
            float boxweight = exp(baseoffset_dot * curvebias);
            rectboxmin = min(rectboxmin, samplecolor);
            rectboxmax = max(rectboxmax, samplecolor);
            vec3 wsample = samplecolor * boxweight;
            rectboxcenter += wsample;
            rectboxvar += (samplecolor * wsample);
            rectboxweight += boxweight;
        }
        {
            vec3 topLeft = texelFetch(InputColor, InputPos + ivec2(-1, 1), 0).xyz;
            vec3 samplecolor = topLeft;
            vec2 baseoffset = srcpos_srcOutputPos + vec2(-1.0, 1.0);
            float baseoffset_dot = dot(baseoffset, baseoffset);
            float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
            float weight = FastLanczos(base);
            Upsampledcw += vec4(samplecolor * weight, weight);
            float boxweight = exp(baseoffset_dot * curvebias);
            rectboxmin = min(rectboxmin, samplecolor);
            rectboxmax = max(rectboxmax, samplecolor);
            vec3 wsample = samplecolor * boxweight;
            rectboxcenter += wsample;
            rectboxvar += (samplecolor * wsample);
            rectboxweight += boxweight;
        }
        {
            vec3 btmRight = texelFetch(InputColor, InputPos + ivec2(1, -1) , 0).xyz;
            vec3 samplecolor = btmRight;
            vec2 baseoffset = srcpos_srcOutputPos + vec2(1.0, -1.0);
            float baseoffset_dot = dot(baseoffset, baseoffset);
            float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
            float weight = FastLanczos(base);
            Upsampledcw += vec4(samplecolor * weight, weight);
            float boxweight = exp(baseoffset_dot * curvebias);
            rectboxmin = min(rectboxmin, samplecolor);
            rectboxmax = max(rectboxmax, samplecolor);
            vec3 wsample = samplecolor * boxweight;
            rectboxcenter += wsample;
            rectboxvar += (samplecolor * wsample);
            rectboxweight += boxweight;
        }

        {
            vec3 btmLeft = texelFetch(InputColor, InputPos + ivec2(-1, -1) , 0).xyz;
            vec3 samplecolor = btmLeft;
            vec2 baseoffset = srcpos_srcOutputPos + vec2(-1.0, -1.0);
            float baseoffset_dot = dot(baseoffset, baseoffset);
            float base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
            float weight = FastLanczos(base);
            Upsampledcw += vec4(samplecolor * weight, weight);
            float boxweight = exp(baseoffset_dot * curvebias);
            rectboxmin = min(rectboxmin, samplecolor);
            rectboxmax = max(rectboxmax, samplecolor);
            vec3 wsample = samplecolor * boxweight;
            rectboxcenter += wsample;
            rectboxvar += (samplecolor * wsample);
            rectboxweight += boxweight;
        }
    }

    rectboxweight = 1.0 / rectboxweight;
    rectboxcenter *= rectboxweight;
    rectboxvar *= rectboxweight;
    rectboxvar = sqrt(abs(rectboxvar - rectboxcenter * rectboxcenter));

    Upsampledcw.xyz =  clamp(Upsampledcw.xyz / Upsampledcw.w, rectboxmin-vec3(0.075), rectboxmax+vec3(0.075));
    Upsampledcw.w = Upsampledcw.w * (1.0f / 3.0f) ;

    float baseupdate = 1.0f - depthfactor;
    baseupdate = min(baseupdate, mix(baseupdate, Upsampledcw.w *10.0f, clamp(10.0f* motion_viewport_len, 0.0, 1.0)));
    baseupdate = min(baseupdate, mix(baseupdate, Upsampledcw.w, clamp(motion_viewport_len *0.05f, 0.0, 1.0)));
    float basealpha = baseupdate;

    const float EPSILON = 1.192e-07f;
    float boxscale = max(depthfactor, clamp(motion_viewport_len * 0.05f, 0.0, 1.0));
    float boxsize = mix(scalefactor, 1.0f, boxscale);
    vec3 sboxvar = rectboxvar * boxsize;
    vec3 boxmin = rectboxcenter - sboxvar;
    vec3 boxmax = rectboxcenter + sboxvar;
    rectboxmax = min(rectboxmax, boxmax);
    rectboxmin = max(rectboxmin, boxmin);

    vec3 clampedcolor = clamp(HistoryColor, rectboxmin, rectboxmax);
    float startLerpValue = params.minLerpContribution;
    if ((abs(mda.x) + abs(mda.y)) > 0.000001) startLerpValue = 0.0;
    float lerpcontribution = (any(greaterThan(rectboxmin, HistoryColor)) || any(greaterThan(HistoryColor, rectboxmax))) ? startLerpValue : 1.0f;

    HistoryColor = mix(clampedcolor, HistoryColor, clamp(lerpcontribution, 0.0, 1.0));
    float basemin = min(basealpha, 0.1f);
    basealpha = mix(basemin, basealpha, clamp(lerpcontribution, 0.0, 1.0));

    ////blend color
    float alphasum = max(EPSILON, basealpha + Upsampledcw.w);
    float alpha = clamp(Upsampledcw.w / alphasum + params.reset, 0.0, 1.0);

    Upsampledcw.xyz = mix(HistoryColor, Upsampledcw.xyz, alpha);

    Output = vec4(Upsampledcw.xyz, 0.0);
}
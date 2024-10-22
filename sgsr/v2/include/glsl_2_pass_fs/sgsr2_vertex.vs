#version 320 es

//============================================================================================================
//
//
//                  Copyright (c) 2024, Qualcomm Innovation Center, Inc. All rights reserved.
//                              SPDX-License-Identifier: BSD-3-Clause
//
//============================================================================================================

precision highp float;
precision highp int;
layout (location = 0) in vec3 vPosition;
layout (location = 1) in vec2 vTexCord;

out vec2 texCoord;
void main()
{
    gl_Position = vec4(vPosition,1.0);
    texCoord  = vTexCord;
}
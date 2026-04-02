package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.POST

interface PushTokenApiService {
    @POST("api/push/register")
    suspend fun register(@Body request: RegisterPushTokenRequest): Response<RegisterPushTokenResponse>
}


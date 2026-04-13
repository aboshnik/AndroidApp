package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.POST

interface AuthApiService {
    @POST("api/auth/register")
    suspend fun register(@Body request: AuthRegisterRequest): Response<AuthRegisterResponse>
}


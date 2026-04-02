package com.example.app.api

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.POST

interface SecurityApiService {
    @POST("api/security/approve")
    suspend fun approve(@Body request: SecurityDecisionRequest): Response<SecurityDecisionResponse>

    @POST("api/security/deny")
    suspend fun deny(@Body request: SecurityDecisionRequest): Response<SecurityDecisionResponse>
}


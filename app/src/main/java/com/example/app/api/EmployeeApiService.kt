package com.example.app.api

import retrofit2.Response
import retrofit2.http.GET
import retrofit2.http.Body
import retrofit2.http.POST
import retrofit2.http.Query

interface EmployeeApiService {
    @POST("api/employee/verify")
    suspend fun verifyEmployee(@Body request: EmployeeVerifyRequest): Response<EmployeeVerifyResponse>

    @POST("api/employee/register")
    suspend fun registerEmployee(@Body request: EmployeeVerifyRequest): Response<EmployeeVerifyResponse>

    @GET("api/employee/profile")
    suspend fun getProfile(
        @Query("employeeId") employeeId: String? = null,
        @Query("login") login: String? = null
    ): Response<EmployeeProfileResponse>

    @GET("api/employee/work-schedule")
    suspend fun getWorkSchedule(
        @Query("employeeId") employeeId: String? = null,
        @Query("login") login: String? = null
    ): Response<EmployeeWorkScheduleResponse>

    @POST("api/employee/login")
    suspend fun login(@Body request: EmployeeLoginRequest): Response<EmployeeLoginResponse>
}


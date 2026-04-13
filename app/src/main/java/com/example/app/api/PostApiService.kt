package com.example.app.api

import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Path
import retrofit2.http.Query

interface PostApiService {
    @POST("api/post")
    suspend fun createPost(@Body request: CreatePostRequest): Response<CreatePostResponse>

    @Multipart
    @POST("api/post/media")
    suspend fun createPostWithMedia(
        @Part("content") content: RequestBody,
        @Part("authorLogin") authorLogin: RequestBody,
        @Part("isImportant") isImportant: RequestBody,
        @Part("pollJson") pollJson: RequestBody?,
        @Part media: List<MultipartBody.Part>
    ): Response<CreatePostResponse>

    @GET("api/post/feed")
    suspend fun getFeed(
        @Query("login") login: String? = null
    ): Response<FeedResponse>

    @DELETE("api/post/{id}")
    suspend fun deletePost(
        @Path("id") id: Int,
        @Query("login") login: String
    ): Response<DeletePostResponse>

    @POST("api/post/{id}/vote")
    suspend fun vote(
        @Path("id") id: Int,
        @Body request: VoteRequest
    ): Response<VoteResponse>
}

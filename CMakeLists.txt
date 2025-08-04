cmake_minimum_required( VERSION 3.24 )


project(TRMesh LANGUAGES CXX VERSION 0.1.0)
add_library(${PROJECT_NAME} STATIC)
target_compile_features(${PROJECT_NAME} PRIVATE cxx_std_20)

include(cmake/CompilerWarnings.cmake)

target_sources(
   ${PROJECT_NAME}
   PRIVATE
      src/test.cpp
)
target_include_directories(
   ${PROJECT_NAME}
   PUBLIC
      include/
)


# Dependencies
set(PMP_BUILD_EXAMPLES OFF)
set(PMP_BUILD_TESTS OFF)
set(PMP_INSTALL OFF)
set(BUILD_SHARED_LIBS OFF)
add_subdirectory(vendor/pmp-library-fork SYSTEM)
# visibility needs to be the same as the main library
set_target_properties(pmp PROPERTIES CXX_VISIBILITY_PRESET hidden)
target_link_libraries(
   ${PROJECT_NAME}
   PRIVATE
      pmp
)



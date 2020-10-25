#!/usr/bin/env bash

dotnet build ./RabbitMQ.sln

for _dir in 'RabbitMQ' 'RabbitMQ2'
do
    pushd "$_dir"
    for _act in '' 'per_thread' 'per_thread_with_lock' 'single'
    do
        dotnet run "$_act"
    done
    popd
done
